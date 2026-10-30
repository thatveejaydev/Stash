using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
{
    public partial class MainForm : Form
    {
        private void CheckToolsStatus()
        {
            ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
            ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");

            bool hasYtDlp = File.Exists(ytDlpPath);
            bool hasFfmpeg = File.Exists(ffmpegPath);

            if (hasYtDlp && hasFfmpeg)
            {
                lblToolsStatus.Text = "● Engine Ready";
                lblToolsStatus.ForeColor = Theme.AccentSuccess;
                btnInstallTools.Text = "Update Engine";
                btnInstallTools.Size = new Size(Dpi.Px(108), Dpi.Px(27));
                btnInstallTools.BaseColor = Theme.BgTopBar;
                btnInstallTools.HoverColor = Theme.BgSurfaceHover;
                btnInstallTools.BorderColor = Color.Empty;
                btnInstallTools.ForeColor = Theme.TextSecondary;
            }
            else
            {
                List<string> missing = new List<string>();
                if (!hasYtDlp) missing.Add("yt-dlp");
                if (!hasFfmpeg) missing.Add("FFmpeg");
                lblToolsStatus.Text = "● Missing: " + string.Join(" & ", missing);
                lblToolsStatus.ForeColor = Theme.AccentWarning;
                btnInstallTools.Text = "Install yt-dlp + FFmpeg";
                btnInstallTools.Size = new Size(Dpi.Px(165), Dpi.Px(27));
                btnInstallTools.BaseColor = Theme.BrandPurple;
                btnInstallTools.HoverColor = Theme.BrandPurpleHover;
                btnInstallTools.BorderColor = Color.Empty;
                btnInstallTools.ForeColor = Color.White;
            }
        }

        private async void BtnInstallTools_Click(object sender, EventArgs e)
        {
            if (!btnInstallTools.Enabled) return;
            btnInstallTools.Enabled = false;
            try
            {
                if (!Directory.Exists(binFolder)) Directory.CreateDirectory(binFolder);

                string targetYtDlp = Path.Combine(binFolder, "yt-dlp.exe");

                if (!File.Exists(ytDlpPath))
                {
                    AppendLog("Downloading latest yt-dlp.exe from GitHub releases...");
                    lblToolsStatus.Text = "● Downloading yt-dlp.exe...";
                    await ExternalToolService.DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", targetYtDlp);
                    AppendLog("yt-dlp.exe installed to " + targetYtDlp);
                }
                else
                {
                    AppendLog("Checking for yt-dlp updates (-U)...");
                    lblToolsStatus.Text = "● Updating yt-dlp...";
                    await Task.Run(() =>
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(ytDlpPath, "-U")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true
                            };
                            using (Process p = Process.Start(psi))
                            {
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(output))
                                    AppendLog(output.Trim());
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Update check failed: " + ex.Message);
                        }
                    });
                }

                ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
                ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    AppendLog("Downloading FFmpeg static build (yt-dlp/FFmpeg-Builds)...");
                    lblToolsStatus.Text = "● Downloading FFmpeg (~80 MB)...";
                    string zipPath = Path.Combine(binFolder, "ffmpeg.zip");
                    string ffmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
                    await ExternalToolService.DownloadFileAsync(ffmpegUrl, zipPath);

                    AppendLog("Extracting ffmpeg.exe and ffprobe.exe into ./bin...");
                    lblToolsStatus.Text = "● Extracting FFmpeg...";
                    await Task.Run(() =>
                    {
                        string extractScript = string.Format(
                            "Add-Type -AssemblyName System.IO.Compression.FileSystem; " +
                            "$zip = [System.IO.Compression.ZipFile]::OpenRead('{0}'); " +
                            "foreach ($entry in $zip.Entries) {{ " +
                            "  if ($entry.Name -eq 'ffmpeg.exe' -or $entry.Name -eq 'ffprobe.exe') {{ " +
                            "    $dest = Join-Path '{1}' $entry.Name; " +
                            "    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true); " +
                            "  }} " +
                            "}} " +
                            "$zip.Dispose(); Remove-Item '{0}' -Force",
                            zipPath.Replace("'", "''"), binFolder.Replace("'", "''"));

                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + extractScript + "\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                        }
                    });
                    AppendLog("FFmpeg ready at " + binFolder);
                }

                CheckToolsStatus();
            }
            catch (Exception ex)
            {
                AppendLog("Error installing tools: " + ex.Message);
                MessageBox.Show("Failed to download dependencies:\n" + ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CheckToolsStatus();
            }
            finally
            {
                btnInstallTools.Enabled = true;
            }
        }

        private void PasteFromClipboardAndDownload()
        {
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show(
                    "Your clipboard is empty. Copy a video or audio URL first, then click 'Paste Link' or press Ctrl+V.",
                    "No Link in Clipboard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string rawInput = Clipboard.GetText().Trim();
            if (!DownloadUrlValidator.IsValid(rawInput))
            {
                MessageBox.Show(
                    "Please copy a valid video or audio link starting with http:// or https://, then try again. Copy only one link at a time.",
                    "Invalid Link",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath))
            {
                DialogResult dr = MessageBox.Show(
                    "yt-dlp.exe and/or ffmpeg.exe are not installed yet.\n\nWould you like to automatically download and install them right now?",
                    "Install Required Dependencies",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                {
                    BtnInstallTools_Click(this, EventArgs.Empty);
                }
                return;
            }

            string outFolder = settings.CurrentOutputFolder;
            if (!Directory.Exists(outFolder))
            {
                try
                {
                    Directory.CreateDirectory(outFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create output folder:\n" + ex.Message, "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            DownloadQueueItem item = new DownloadQueueItem(
                rawInput,
                mode,
                res,
                embedMeta,
                embedThumb,
                concurrentFragments,
                badgeLabel,
                outFolder);
            item.EmbedSubtitles = settings.EmbedSubtitles;
            item.IncludeAutoCaptions = settings.IncludeAutoCaptions;
            bool isVideoMode = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            if (isVideoMode && (item.EmbedSubtitles || item.IncludeAutoCaptions))
            {
                item.BadgeLabel += "   ·   SUBS";
            }

            lock (queueLock)
            {
                queue.Insert(0, item);
            }

            queueListView.InsertItemAtTop(item);
            UpdateQueueHeaderStats();

            AppendLog(string.Format("[Queue] Pasted & Added: {0} [{1}] -> {2}", item.Url, item.BadgeLabel, outFolder));
            ProcessQueue();
        }

        private void UpdateQueueHeaderStats()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateQueueHeaderStats);
                return;
            }

            int total = queue.Count;
            int running = queue.FindAll(x => x.Status == QueueItemStatus.Downloading || x.Status == QueueItemStatus.Merging || x.Status == QueueItemStatus.FetchingInfo).Count;
            int done = queue.FindAll(x => x.Status == QueueItemStatus.Completed).Count;
            lblActiveDownloads.Text = string.Format("·  {0} active download{1}", running, running == 1 ? "" : "s");

            if (running > 0)
                lblQueueStats.Text = string.Format("QUEUE  •  {0} ACTIVE  •  {1} OF {2} COMPLETED", running, done, total);
            else
                lblQueueStats.Text = string.Format("QUEUE  •  {0} ITEMS  ({1} COMPLETED)", total, done);
        }

        private void ProcessQueue()
        {
            int maxParallel = settings.ParallelCount;

            lock (queueLock)
            {
                while (activeDownloads < maxParallel)
                {
                    DownloadQueueItem nextItem = queue.Find(x => x.Status == QueueItemStatus.Queued);
                    if (nextItem == null) break;

                    nextItem.Status = QueueItemStatus.FetchingInfo;
                    nextItem.StatusText = "Fetching info...";
                    activeDownloads++;
                    RefreshQueueUi();

                    Task.Run(() => ExecuteDownloadAsync(nextItem));
                }
            }
        }

        private bool CheckAndPromptForDuplicateRename(DownloadQueueItem item, string fetchedTitle, string fetchedId)
        {
            string cleanTitle = StorageFileService.SanitizeFileName(fetchedTitle);
            if (string.IsNullOrEmpty(cleanTitle)) return true;

            string existingFile = StorageFileService.FindSimilarExistingFile(item.OutputFolder, cleanTitle, fetchedId);
            if (existingFile == null)
            {
                item.CustomBaseFileName = cleanTitle;
                return true;
            }

            string ext = YtDlpCommandBuilder.GetTargetExtension(item.Mode);
            string suggested = StorageFileService.SuggestNextAvailableName(item.OutputFolder, cleanTitle, ext);

            bool accepted = false;
            Invoke((Action)(() =>
            {
                using (RenamePromptDialog dlg = new RenamePromptDialog(existingFile, suggested, ext))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        item.CustomBaseFileName = dlg.ChosenBaseName;
                        item.Title = dlg.ChosenBaseName;
                        accepted = true;
                    }
                }
            }));

            return accepted;
        }

        private async Task ExecuteDownloadAsync(DownloadQueueItem item)
        {
            try
            {
                if (!item.MetadataResolved)
                {
                    Tuple<string, string> meta = await ExternalToolService.FetchMetadataTitleAndIdAsync(ytDlpPath, item, RefreshQueueUi);

                    if (item.CancellationSource.IsCancellationRequested)
                    {
                        if (item.IsPauseRequested) MarkPaused(item);
                        else MarkCancelled(item);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(meta.Item1))
                    {
                        bool proceed = CheckAndPromptForDuplicateRename(item, meta.Item1, meta.Item2);
                        if (!proceed)
                        {
                            MarkCancelled(item);
                            return;
                        }
                        item.MetadataResolved = true;
                        RefreshQueueUi();
                    }
                }

                item.Status = QueueItemStatus.Downloading;
                item.StatusText = item.ProgressPercent > 0 ? "Resuming..." : "Starting...";
                RefreshQueueUi();

                Directory.CreateDirectory(item.TemporaryFolder);
                string arguments = YtDlpCommandBuilder.BuildArguments(item, ffmpegPath);
                AppendLog(string.Format("[{0}] yt-dlp {1}", item.Id, arguments));

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    item.ActiveProcess = proc;

                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            YtDlpOutputParser.ParseLine(item, e.Data, RefreshQueueUi, AppendLog);
                        }
                    };

                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendLog(string.Format("[{0} ERR] {1}", item.Id, e.Data));
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    while (!proc.HasExited)
                    {
                        if (item.CancellationSource.IsCancellationRequested)
                        {
                            ExternalToolService.KillProcessTree(proc.Id);
                            if (item.IsPauseRequested) MarkPaused(item);
                            else MarkCancelled(item);
                            return;
                        }
                        await Task.Delay(150);
                    }

                    proc.WaitForExit();

                    if (item.CancellationSource.IsCancellationRequested)
                    {
                        if (item.IsPauseRequested) MarkPaused(item);
                        else MarkCancelled(item);
                    }
                    else if (proc.ExitCode == 0)
                    {
                        await StorageFileService.CleanupPartialFilesForItemAsync(item);
                        item.ProgressPercent = 100.0;
                        item.Speed = "—";
                        item.Eta = "00:00";
                        item.Status = QueueItemStatus.Completed;
                        item.StatusText = "Completed";
                        RefreshQueueUi();
                        AppendLog(string.Format("[{0}] Completed: {1}", item.Id, item.Title));
                    }
                    else
                    {
                        item.Status = QueueItemStatus.Error;
                        item.StatusText = "Failed (Exit " + proc.ExitCode + ")";
                        RefreshQueueUi();
                        AppendLog(string.Format("[{0}] Failed with exit code {1}", item.Id, proc.ExitCode));
                    }
                }
            }
            catch (Exception ex)
            {
                if (item.CancellationSource.IsCancellationRequested)
                {
                    if (item.IsPauseRequested) MarkPaused(item);
                    else MarkCancelled(item);
                }
                else
                {
                    item.Status = QueueItemStatus.Error;
                    item.StatusText = "Error";
                    RefreshQueueUi();
                    AppendLog(string.Format("[{0}] Error: {1}", item.Id, ex.Message));
                }
            }
            finally
            {
                item.ActiveProcess = null;
                lock (queueLock)
                {
                    activeDownloads = Math.Max(0, activeDownloads - 1);
                }
                BeginInvoke((Action)(() =>
                {
                    UpdateQueueHeaderStats();
                    ProcessQueue();
                }));
            }
        }

        private void MarkPaused(DownloadQueueItem item)
        {
            item.Status = QueueItemStatus.Paused;
            item.StatusText = "Paused";
            item.Speed = "—";
            item.Eta = "—";
            RefreshQueueUi();
            AppendLog(string.Format("[{0}] Paused at {1:0.0}%: {2}", item.Id, item.ProgressPercent, item.Title));
        }

        private void MarkCancelled(DownloadQueueItem item)
        {
            item.Status = QueueItemStatus.Cancelled;
            item.StatusText = "Cancelled";
            item.Speed = "—";
            item.Eta = "—";
            RefreshQueueUi();
            AppendLog(string.Format("[{0}] Cancelled: {1}", item.Id, item.Title));
            Task.Run(async () =>
            {
                int deleted = await StorageFileService.CleanupPartialFilesForItemAsync(item);
                if (deleted > 0)
                {
                    AppendLog(string.Format("[{0}] Cleaned up {1} temporary fragment file(s).", item.Id, deleted));
                }
            });
        }

        private void TogglePauseResumeItem(DownloadQueueItem item)
        {
            if (item == null) return;

            if (item.Status == QueueItemStatus.Downloading ||
                item.Status == QueueItemStatus.FetchingInfo ||
                item.Status == QueueItemStatus.Queued)
            {
                item.IsPauseRequested = true;
                item.CancellationSource.Cancel();
                if (item.Status == QueueItemStatus.Queued)
                {
                    MarkPaused(item);
                }
                else if (item.ActiveProcess != null && !item.ActiveProcess.HasExited)
                {
                    ExternalToolService.KillProcessTree(item.ActiveProcess.Id);
                }
            }
            else if (item.Status == QueueItemStatus.Paused)
            {
                item.IsPauseRequested = false;
                item.CancellationSource = new CancellationTokenSource();
                item.Status = QueueItemStatus.Queued;
                item.StatusText = "Resuming...";
                RefreshQueueUi();
                AppendLog(string.Format("[{0}] Resumed: {1}", item.Id, item.Title));
                ProcessQueue();
            }
        }

        private void CancelSingleItem(DownloadQueueItem item)
        {
            if (item == null) return;
            if (item.Status == QueueItemStatus.Queued ||
                item.Status == QueueItemStatus.FetchingInfo ||
                item.Status == QueueItemStatus.Downloading ||
                item.Status == QueueItemStatus.Merging ||
                item.Status == QueueItemStatus.Paused)
            {
                item.IsPauseRequested = false;
                item.CancellationSource.Cancel();
                if (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.Paused)
                {
                    MarkCancelled(item);
                }
                else if (item.ActiveProcess != null && !item.ActiveProcess.HasExited)
                {
                    ExternalToolService.KillProcessTree(item.ActiveProcess.Id);
                }
            }
        }

        private void OpenCompletedItemFolder(DownloadQueueItem item)
        {
            if (item == null) return;
            try
            {
                if (!string.IsNullOrEmpty(item.OutputFilePath) && File.Exists(item.OutputFilePath))
                {
                    Process.Start("explorer.exe", "/select,\"" + item.OutputFilePath + "\"");
                }
                else if (Directory.Exists(item.OutputFolder))
                {
                    Process.Start("explorer.exe", item.OutputFolder);
                }
            }
            catch { }
        }

        private void BtnCancelSelected_Click(object sender, EventArgs e)
        {
            CancelSingleItem(queueListView.SelectedItem);
        }

        private void BtnRemoveFinished_Click(object sender, EventArgs e)
        {
            List<DownloadQueueItem> current = queueListView.GetItems();
            foreach (DownloadQueueItem item in current)
            {
                if (item.Status == QueueItemStatus.Completed ||
                    item.Status == QueueItemStatus.Cancelled ||
                    item.Status == QueueItemStatus.Error)
                {
                    lock (queueLock)
                    {
                        queue.Remove(item);
                    }
                    queueListView.RemoveItem(item);
                }
            }
            UpdateQueueHeaderStats();
        }

        private void RefreshQueueUi()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshQueueUi);
                return;
            }
            queueListView.Invalidate();
            UpdateQueueHeaderStats();
        }

        private void AppendLog(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => AppendLog(message)));
                return;
            }

            string line = string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine);
            txtLog.AppendText(line);
            txtLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            settings.Save();
            lock (queueLock)
            {
                foreach (var item in queue)
                {
                    try
                    {
                        item.CancellationSource.Cancel();
                        if (item.ActiveProcess != null && !item.ActiveProcess.HasExited)
                        {
                            ExternalToolService.KillProcessTree(item.ActiveProcess.Id);
                        }
                    }
                    catch { }
                }
            }
            base.OnFormClosing(e);
        }
    }
}
