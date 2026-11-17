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
        private enum InputSource { Paste, Drop, ClipboardWatch }

        private void PasteFromClipboardAndDownload()
        {
            string rawInput = ReadClipboardText();
            if (rawInput == null)
            {
                MessageBox.Show(this,
                    "Your clipboard is empty or busy. Copy a video, audio, or playlist link first, then click 'Paste Link' or press Ctrl+V.",
                    "No Link in Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ProcessInputText(rawInput, InputSource.Paste);
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

        private void ProcessInputText(string rawInput, InputSource source)
        {
            bool silent = source == InputSource.ClipboardWatch;
            List<string> extractedUrls = DownloadUrlValidator.ExtractUrls(rawInput);
            if (extractedUrls.Count == 0)
            {
                if (!silent)
                    MessageBox.Show(this,
                        "Please use a valid video, audio, or playlist link starting with http:// or https://, then try again.",
                        "Invalid Link", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath))
            {
                if (engineOperationRunning)
                {
                    ShowNotice("Engines are still installing. Add the link again when they are ready.");
                    return;
                }
                if (silent) return;
                DialogResult dr = MessageBox.Show(this,
                    "yt-dlp.exe and/or ffmpeg.exe are not installed yet.\n\nWould you like to automatically download and install them right now?",
                    "Install Required Dependencies", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr == DialogResult.Yes) RunEngineOperationAsync(false);
                return;
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

            if (singles.Count > 0) QueueUrls(singles, null, null);

            // Each playlist or channel gets its own picker instead of being downloaded as one item.
            foreach (string playlist in playlists)
            {
                if (silent) { Activate(); }
                using (PlaylistPromptDialog dlg = new PlaylistPromptDialog(playlist, ytDlpPath, settings.BrowserCookies))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        QueueUrls(dlg.ResultUrls, dlg.ResultTitles, dlg.ResultIds);
                }
            }
        }

        private bool IsAlreadyQueued(string url)
        {
            foreach (DownloadQueueItem item in downloadManager.GetItems())
                if (DownloadManager.IsUnfinished(item.Status) && string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private void QueueUrls(IList<string> urls, IList<string> preResolvedTitles, IList<string> sourceIds)
        {
            if (urls == null || urls.Count == 0) return;

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
                    return;
                }
            }

            settings.AddFolderToHistory(outFolder);

            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            VideoResolution res = (VideoResolution)settings.QualityIndex;
            bool embedMeta = settings.EmbedMetadata;
            bool embedThumb = settings.EmbedThumbnail;
            int concurrentFragments = settings.ConcurrentFragments;
            string badgeLabel = YtDlpCommandBuilder.BuildBadgeLabel(mode, res, embedMeta, embedThumb, concurrentFragments);
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
                return;
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
                if (!string.Equals(other.CustomBaseFileName, baseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(YtDlpCommandBuilder.GetTargetExtension(other.Mode), ext, StringComparison.OrdinalIgnoreCase)) continue;
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

        private void OpenCompletedItemFolder(DownloadQueueItem item)
        {
            if (item == null) return;
            try
            {
                if (!string.IsNullOrEmpty(item.OutputFilePath) && File.Exists(item.OutputFilePath))
                {
                    Process.Start("explorer.exe", "/select," + CommandLine.Quote(item.OutputFilePath));
                }
                else if (Directory.Exists(item.OutputFolder))
                {
                    Process.Start("explorer.exe", CommandLine.Quote(item.OutputFolder));
                }
                else ShowNotice("The download folder no longer exists.");
            }
            catch (Exception ex) { AppLog.Error("Could not open folder", ex); }
        }

        private void OpenCompletedFile(DownloadQueueItem item)
        {
            if (item == null) return;
            if (string.IsNullOrEmpty(item.OutputFilePath) || !File.Exists(item.OutputFilePath))
            {
                ShowNotice("The downloaded file was moved or deleted.");
                return;
            }
            try { Process.Start(new ProcessStartInfo(item.OutputFilePath) { UseShellExecute = true }); }
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
