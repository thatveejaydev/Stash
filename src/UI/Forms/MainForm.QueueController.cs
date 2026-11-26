using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private enum InputSource { Paste, ClipboardWatch }

        private void StageClipboardLinks(bool insert)
        {
            string rawInput = ReadClipboardText();
            if (rawInput == null)
            {
                linkBar.ShowStatus("Copy a link first, or paste one into the field.", Theme.AccentWarning);
                return;
            }
            linkBar.Stage(rawInput, insert);
        }

        private void DownloadClipboardLinks()
        {
            string rawInput = ReadClipboardText();
            if (rawInput == null)
            {
                linkBar.ShowStatus("Copy a link first, or paste one into the field.", Theme.AccentWarning);
                return;
            }
            // Submit through the link field so batches keep any links that still need attention.
            linkBar.Stage(rawInput, false);
            SubmitLink(linkBar.LinkText);
        }

        // Another application can hold the clipboard open briefly; retry instead of crashing.
        private static string ReadClipboardText()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsText()) return null;
                    string text = Clipboard.GetText();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
                catch (ExternalException)
                {
                    System.Threading.Thread.Sleep(40);
                }
            }
            return null;
        }

        // A null result keeps the original draft. A partial batch keeps only links
        // that still need attention, so submitting again doesn't repeat finished work.
        private List<string> ProcessInputText(string rawInput, InputSource source, TranscriptCaptions.Source inspected = null)
        {
            bool silent = source == InputSource.ClipboardWatch;
            List<string> extractedUrls = DownloadUrlValidator.ExtractUrls(rawInput);
            if (extractedUrls.Count == 0) return null;

            if (!DownloadToolsReady)
            {
                if (engineOperationRunning)
                {
                    if (!silent) linkBar.ShowStatus("Preparing download engines…", Theme.TextSecondary);
                    return null;
                }
                if (silent) return null;
                DialogResult dr = MessageBox.Show(this,
                    "yt-dlp.exe and/or ffmpeg.exe are not installed yet.\n\nWould you like to automatically download and install them right now?",
                    "Install Required Dependencies", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.Yes) RunEngineOperationAsync(false);
                return null;
            }

            List<string> singles = new List<string>();
            List<string> playlists = new List<string>();
            foreach (string url in extractedUrls)
            {
                string singleUrl, videoId;
                if (!DownloadUrlValidator.IsPlaylistUrl(url)) singles.Add(url);
                else if (extractedUrls.Count > 1 && url.IndexOf("list=", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    DownloadUrlValidator.TryExtractSingleVideoUrl(url, out singleUrl, out videoId))
                    singles.Add(singleUrl); // A watched video inside a playlist, pasted among other links.
                else playlists.Add(url);
            }

            List<string> remaining = new List<string>();
            if (singles.Count > 0 && !QueueUrls(singles, null, null, inspected)) remaining.AddRange(singles);

            // Each playlist or channel gets its own picker instead of being downloaded as one item.
            foreach (string playlist in playlists)
            {
                if (silent) { Activate(); }
                using (PlaylistPromptDialog dlg = new PlaylistPromptDialog(playlist, ytDlpPath, settings.BrowserCookies))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK ||
                        !QueueUrls(dlg.ResultUrls, dlg.ResultTitles, dlg.ResultIds))
                        remaining.Add(playlist);
                }
            }
            return remaining.Count == extractedUrls.Count ? null : remaining;
        }

        private bool IsAlreadyQueued(string url)
        {
            foreach (DownloadQueueItem item in downloadManager.GetItems())
            {
                if (!DownloadManager.IsUnfinished(item.Status) || !string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsTranscriptMode != (item.Mode == DownloadMode.TranscriptOnly)) continue;
                if (!IsTranscriptMode || item.TranscriptFormat == settings.TranscriptFormat &&
                    string.Equals(item.TranscriptLanguage, settings.TranscriptLanguage, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private bool QueueUrls(IList<string> urls, IList<string> preResolvedTitles, IList<string> sourceIds,
            TranscriptCaptions.Source inspected = null)
        {
            if (urls == null || urls.Count == 0) return false;

            string outFolder = settings.CurrentOutputFolder;
            if (!Directory.Exists(outFolder))
            {
                try
                {
                    Directory.CreateDirectory(outFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not create output folder:\n" + ex.Message, "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            settings.AddFolderToHistory(outFolder);

            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            VideoResolution res = (VideoResolution)settings.QualityIndex;
            bool embedMeta = settings.EmbedMetadata;
            bool embedThumb = settings.EmbedThumbnail;
            int concurrentFragments = settings.ConcurrentFragments;
            string badgeLabel = YtDlpCommandBuilder.BuildBadgeLabel(mode, res, embedMeta, embedThumb, concurrentFragments);
            if (mode == DownloadMode.TranscriptOnly)
                badgeLabel = "TRANSCRIPT  ·  " + TranscriptFormatNames[(int)settings.TranscriptFormat] + "  ·  " +
                    CaptionLanguageName(settings.TranscriptLanguage);
            bool isVideoMode = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            if (isVideoMode && (settings.EmbedSubtitles || settings.IncludeAutoCaptions))
            {
                badgeLabel += "   ·   SUBS";
            }

            List<DownloadQueueItem> newItems = new List<DownloadQueueItem>();
            HashSet<string> batch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            for (int i = 0; i < urls.Count; i++)
            {
                string u = urls[i];
                if (string.IsNullOrWhiteSpace(u)) continue;
                if (!batch.Add(u) || IsAlreadyQueued(u)) { skipped++; continue; }

                DownloadQueueItem item = new DownloadQueueItem(
                    u,
                    mode,
                    res,
                    embedMeta,
                    embedThumb,
                    concurrentFragments,
                    badgeLabel,
                    outFolder);

                item.EmbedSubtitles = settings.EmbedSubtitles;
                item.IncludeAutoCaptions = settings.IncludeAutoCaptions;
                if (mode == DownloadMode.TranscriptOnly)
                {
                    item.TranscriptLanguage = settings.TranscriptLanguage;
                    item.TranscriptFormat = settings.TranscriptFormat;
                    item.InspectedCaptions = inspected;
                }
                item.BrowserCookies = settings.BrowserCookies;
                item.FileNameStyle = settings.FileNameStyle;
                if (sourceIds != null && i < sourceIds.Count) item.SourceId = sourceIds[i];

                if (preResolvedTitles != null && i < preResolvedTitles.Count && !string.IsNullOrWhiteSpace(preResolvedTitles[i]))
                {
                    string clean = StorageFileService.SanitizeFileName(preResolvedTitles[i]);
                    if (!string.IsNullOrEmpty(clean))
                    {
                        item.Title = clean;
                        item.MetadataResolved = true;
                    }
                }

                newItems.Add(item);
            }

            if (newItems.Count == 0)
            {
                if (skipped > 0) ShowNotice(skipped == 1 ? "That link is already in the queue." : "Those links are already in the queue.");
                return skipped > 0;
            }

            downloadManager.AddItems(newItems);

            queueListView.InsertItemsAtTop(newItems);
            UpdateQueueHeaderStats();

            if (newItems.Count == 1)
            {
                AppendLog(string.Format("[Queue] Pasted & Added: {0} [{1}] -> {2}", newItems[0].Url, newItems[0].BadgeLabel, outFolder));
            }
            else
            {
                AppendLog(string.Format("[Batch] Detected and queued {0} links -> {1}", newItems.Count, outFolder));
            }
            if (skipped > 0) ShowNotice(string.Format("{0} added; {1} already in the queue.", newItems.Count, skipped));

            ProcessQueue();
            return true;
        }

        private void UpdateQueueHeaderStats()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateQueueHeaderStats);
                return;
            }

            List<DownloadQueueItem> queue = downloadManager.GetItems();
            int total = queue.Count;
            int running = 0, done = 0, failed = 0, paused = 0, waiting = 0;
            double progress = 0;
            foreach (DownloadQueueItem item in queue)
            {
                if (DownloadManager.IsActive(item.Status)) running++;
                else if (item.Status == QueueItemStatus.Completed) done++;
                else if (item.Status == QueueItemStatus.Error) failed++;
                else if (item.Status == QueueItemStatus.Paused) paused++;
                else if (item.Status == QueueItemStatus.Queued) waiting++;
                if (DownloadManager.IsActive(item.Status) || item.Status == QueueItemStatus.Queued) progress += Math.Max(0, Math.Min(100, item.ProgressPercent));
            }

            string summary = total == 0 ? "" : total == 1 ? "1 item" : total + " items";
            if (running > 0) summary += "  ·  " + running + " downloading";
            if (failed > 0) summary += "  ·  " + failed + " failed";
            lblQueueSummary.Text = summary;

            int busy = running + waiting;
            IntPtr window = IsHandleCreated ? Handle : IntPtr.Zero;
            if (busy > 0) taskbar.SetProgress(window, progress / busy / 100.0, failed > 0);
            else taskbar.Clear(window);

            if (busy > 0) queueWasBusy = true;
            else if (queueWasBusy)
            {
                queueWasBusy = false;
                NotifyQueueFinished(done, failed, paused);
            }
        }

        private void NotifyQueueFinished(int completed, int failed, int paused)
        {
            if (completed == 0 && failed == 0) return;
            if (ContainsFocus && WindowState != FormWindowState.Minimized) return;
            if (trayIcon == null)
            {
                trayIcon = new NotifyIcon { Icon = Icon, Text = "Stash" };
                trayIcon.BalloonTipClicked += (s, e) => RestoreFromNotification();
                trayIcon.Click += (s, e) => RestoreFromNotification();
            }
            trayIcon.Visible = true;
            string message = failed == 0
                ? string.Format("{0} download{1} finished.", completed, completed == 1 ? "" : "s")
                : string.Format("{0} finished, {1} failed.", completed, failed);
            if (paused > 0) message += string.Format(" {0} paused.", paused);
            trayIcon.ShowBalloonTip(5000, "Stash", message, failed == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
            FlashWindow();
        }

        private void RestoreFromNotification()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (trayIcon != null) trayIcon.Visible = false;
        }

        private void ProcessQueue()
        {
            // Never start yt-dlp while the engines are being replaced.
            if (engineOperationRunning || !File.Exists(ytDlpPath)) return;
            downloadManager.ProcessQueue(ytDlpPath, ffmpegPath, settings.ParallelCount);
        }

        // Called before each download starts. Picks the file name and asks about duplicates,
        // including other queued items that would write the same file.
        private bool CheckAndPromptForDuplicateRename(DownloadQueueItem item, string fetchedTitle, string fetchedId)
        {
            string baseName = YtDlpCommandBuilder.BuildBaseFileName(item.FileNameStyle, fetchedTitle, fetchedId, item.Uploader);
            if (string.IsNullOrEmpty(baseName)) return true;

            string ext = YtDlpCommandBuilder.GetTargetExtension(item.Mode);
            Func<string, bool> reserved = candidate => IsNameReservedByQueue(item, candidate, ext);
            string existingFile = StorageFileService.FindSimilarExistingFile(item.OutputFolder, baseName,
                item.FileNameStyle == FileNameStyle.TitleWithId ? null : fetchedId);
            if (existingFile == null && reserved(baseName)) existingFile = baseName + ext + "  (queued in Stash)";
            if (existingFile == null)
            {
                item.CustomBaseFileName = baseName;
                return true;
            }

            string suggested = StorageFileService.SuggestNextAvailableName(item.OutputFolder, baseName, ext, reserved);
            using (RenamePromptDialog dlg = new RenamePromptDialog(existingFile, suggested, ext))
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                item.CustomBaseFileName = dlg.ChosenBaseName;
                item.Title = dlg.ChosenBaseName;
                return true;
            }
        }

        private bool IsNameReservedByQueue(DownloadQueueItem current, string baseName, string ext)
        {
            foreach (DownloadQueueItem other in downloadManager.GetItems())
            {
                if (other == current || !DownloadManager.IsUnfinished(other.Status)) continue;
                // An unfinished item reserves both the name it downloads to and any rename waiting for it to finish.
                if (!string.Equals(other.CustomBaseFileName, baseName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(other.PendingRenameBaseName, baseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(GetExpectedExtension(other), ext, StringComparison.OrdinalIgnoreCase)) continue;
                if (StorageFileService.SameDirectory(other.OutputFolder, current.OutputFolder)) return true;
            }
            return false;
        }

        private void TogglePauseResumeItem(DownloadQueueItem item)
        {
            downloadManager.TogglePauseResume(item);
        }

        private void CancelSingleItem(DownloadQueueItem item)
        {
            if (item == null) return;
            downloadManager.Cancel(item);
        }

        private void HandleSectionAction(QueueSectionAction action)
        {
            if (action == QueueSectionAction.ResumeAll)
            {
                downloadManager.ResumeAll();
                ProcessQueue();
            }
            else if (action == QueueSectionAction.PauseAll) downloadManager.PauseAll();
            else
            {
                foreach (DownloadQueueItem item in queueListView.GetItems())
                    if (item.Status == QueueItemStatus.Completed && downloadManager.RemoveFinished(item)) queueListView.RemoveItem(item);
            }
            queueListView.RefreshChangedItems();
            queueListView.Invalidate();
            UpdateQueueHeaderStats();
        }

        private async void OpenCompletedItemFolder(DownloadQueueItem item)
        {
            if (item == null) return;
            try
            {
                if (!string.IsNullOrEmpty(item.OutputFilePath) && File.Exists(item.OutputFilePath))
                {
                    await ExplorerReveal.ShowFileAsync(item.OutputFilePath);
                }
                else if (Directory.Exists(item.OutputFolder))
                {
                    Process.Start("explorer.exe", CommandLine.Quote(item.OutputFolder));
                }
                else ShowNotice("The download folder no longer exists.");
            }
            catch (Exception ex) { AppLog.Error("Could not open folder", ex); }
        }

        private static string GetExpectedExtension(DownloadQueueItem item)
        {
            return item.Mode == DownloadMode.TranscriptOnly
                ? YtDlpCommandBuilder.GetTranscriptExtension(item.TranscriptFormat)
                : YtDlpCommandBuilder.GetTargetExtension(item.Mode);
        }

        private void RenameFile(DownloadQueueItem item)
        {
            if (item == null) return;
            if (item.Status == QueueItemStatus.Completed) RenameCompletedFile(item);
            else if (DownloadManager.IsUnfinished(item.Status)) RenameWhenFinished(item);
        }

        // The engine still owns the file, so only remember the name; DownloadManager applies it on completion.
        private void RenameWhenFinished(DownloadQueueItem item)
        {
            string extension = GetExpectedExtension(item);
            string current = item.CustomBaseFileName ?? (item.Title != item.Url ? item.Title : "");
            string suggested = item.PendingRenameBaseName ?? current;
            Func<string, string> validate = name =>
            {
                if (StorageFileService.IsReservedDeviceName(name)) return "Windows reserves this name. Choose another.";
                if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) return null;
                string target = Path.Combine(item.OutputFolder, name + extension);
                if (File.Exists(target) || Directory.Exists(target)) return "A file with this name already exists.";
                if (IsNameReservedByQueue(item, name, extension)) return "A queued download will use this name.";
                return null;
            };
            string newBaseName;
            string currentFileName = current.Length == 0 ? "Not known yet" : current + extension;
            using (RenamePromptDialog dialog = new RenamePromptDialog(currentFileName, suggested, extension, validate, true))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                newBaseName = dialog.ChosenBaseName;
            }
            // The download may have finished while the dialog was open.
            if (item.Status == QueueItemStatus.Completed)
            {
                ApplyRename(item, item.OutputFilePath, newBaseName);
                return;
            }
            bool unchanged = string.Equals(newBaseName, current, StringComparison.Ordinal);
            if (unchanged && item.PendingRenameBaseName == null) return;
            item.PendingRenameBaseName = unchanged ? null : newBaseName;
            queueListView.RefreshChangedItems();
            queueListView.Invalidate();
            SaveQueue();
            ShowNotice(unchanged ? "The download will keep its original name."
                : "Will be renamed to " + newBaseName + extension + " when the download finishes.");
        }

        private void OnPendingRenameFailed(DownloadQueueItem item, string newBaseName, Exception error)
        {
            ShowNotice("The finished download kept its original name. " + DescribeRenameError(error));
        }

        private static string DescribeRenameError(Exception error)
        {
            const int SharingViolation = unchecked((int)0x80070020);
            if (error is FileNotFoundException) return "The downloaded file was moved or deleted.";
            if (error is PathTooLongException) return "That name is too long for this folder.";
            if (error is UnauthorizedAccessException) return "Stash doesn't have permission to rename this file.";
            if (error is IOException && Marshal.GetHRForException(error) == SharingViolation)
                return "The file is open in another app. Close it and try again.";
            return "Could not rename the file: " + error.Message;
        }

        private void RenameCompletedFile(DownloadQueueItem item)
        {
            if (item == null || item.Status != QueueItemStatus.Completed) return;
            string path = item.OutputFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowNotice("The downloaded file was moved or deleted.");
                return;
            }
            string folder = Path.GetDirectoryName(path);
            string extension = Path.GetExtension(path);
            Func<string, string> validate = name =>
            {
                if (StorageFileService.IsReservedDeviceName(name)) return "Windows reserves this name. Choose another.";
                string target = Path.Combine(folder, name + extension);
                if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase)) return null;
                if (File.Exists(target) || Directory.Exists(target)) return "A file with this name already exists.";
                if (IsNameReservedByQueue(item, name, extension)) return "A queued download will use this name.";
                return null;
            };
            string newBaseName;
            using (RenamePromptDialog dialog = new RenamePromptDialog(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path), extension, validate))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                newBaseName = dialog.ChosenBaseName;
            }
            ApplyRename(item, path, newBaseName);
        }

        private void ApplyRename(DownloadQueueItem item, string path, string newBaseName)
        {
            if (string.IsNullOrEmpty(path))
            {
                ShowNotice("The downloaded file was moved or deleted.");
                return;
            }
            try
            {
                string renamed = StorageFileService.RenameDownloadedFile(path, newBaseName);
                if (renamed == path) return;
                item.OutputFilePath = renamed;
                item.Title = newBaseName;
                queueListView.RefreshChangedItems();
                queueListView.Invalidate();
                SaveQueue();
                ShowNotice("Renamed to " + Path.GetFileName(renamed) + ".");
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                ShowNotice(DescribeRenameError(ex));
                if (!(ex is FileNotFoundException) && !(ex is PathTooLongException) && !(ex is UnauthorizedAccessException))
                    AppLog.Error("Could not rename the file", ex);
            }
        }

        private void OpenCompletedFile(DownloadQueueItem item)
        {
            if (item == null) return;
            if (string.IsNullOrEmpty(item.OutputFilePath) || !File.Exists(item.OutputFilePath))
            {
                ShowNotice("The downloaded file was moved or deleted.");
                return;
            }
            try
            {
                if (item.Mode == DownloadMode.TranscriptOnly)
                    Process.Start("notepad.exe", CommandLine.Quote(item.OutputFilePath));
                else Process.Start(new ProcessStartInfo(item.OutputFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not open file", ex);
                OpenCompletedItemFolder(item);
            }
        }

        private void CopyItemUrl(DownloadQueueItem item)
        {
            if (item == null) return;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    ignoreClipboardText = item.Url;
                    Clipboard.SetText(item.Url);
                    ShowNotice("Link copied.");
                    return;
                }
                catch (ExternalException) { System.Threading.Thread.Sleep(40); }
            }
            ShowNotice("The clipboard is busy. Try again.");
        }

        private void RefreshQueueUi()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshQueueUi);
                return;
            }
            queueListView.RefreshChangedItems();
            UpdateQueueHeaderStats();
        }

        private void AppendLog(string message)
        {
            logWriter.Write(message);
            string notification = null;
            if (message.StartsWith("[Queue]")) notification = "Link added to the queue.";
            if (message.StartsWith("[Batch]")) notification = "Links added to the queue.";
            if (notification != null) ShowNotice(notification);
        }

        private void ShowNotice(string text)
        {
            if (IsDisposed || !IsHandleCreated || notice == null) return;
            if (InvokeRequired) { BeginInvoke((Action<string>)ShowNotice, text); return; }
            notice.Text = text;
            notice.Visible = true;
            noticeTimer.Stop();
            noticeTimer.Start();
        }

        private void SaveQueue()
        {
            try { queueStore.Save(downloadManager.GetItems()); }
            catch (Exception ex) { AppLog.Error("Could not save the queue", ex); }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || shutdownComplete) return;
            e.Cancel = true;
            if (!Enabled) return;

            ShutdownMode mode = ShutdownMode.PauseForResume;
            int unfinished = downloadManager.GetItems().FindAll(item => DownloadManager.IsUnfinished(item.Status)).Count;
            int active = downloadManager.GetItems().FindAll(item => DownloadManager.IsActive(item.Status) || item.Status == QueueItemStatus.Queued).Count;
            if (active > 0 && e.CloseReason == CloseReason.UserClosing)
            {
                using (CloseConfirmationDialog dialog = new CloseConfirmationDialog(active, unfinished))
                {
                    DialogResult choice = dialog.ShowDialog(this);
                    if (choice == DialogResult.Cancel) return;
                    mode = choice == DialogResult.Abort ? ShutdownMode.DiscardAll : ShutdownMode.PauseForResume;
                }
            }

            Enabled = false;
            CancelCaptionCheck();
            progressRefreshTimer.Stop();
            engineCancellation.Cancel();
            settings.Save();
            Task shutdown = downloadManager.ShutdownAsync(mode);
            // Never leave the window hanging if an engine process refuses to exit.
            await Task.WhenAny(shutdown, Task.Delay(15000));
            if (!shutdown.IsCompleted) AppLog.Error("Shutdown timed out waiting for download processes.");
            SaveQueue();
            shutdownComplete = true;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CancelCaptionCheck();
            if (clipboardListening) RemoveClipboardFormatListener(Handle);
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            progressRefreshTimer.Dispose();
            AppLog.Connect(null);
            logWriter.Dispose();
            noticeTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
