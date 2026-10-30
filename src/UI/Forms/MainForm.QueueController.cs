using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
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

                    AppendLog("Extracting ffmpeg.exe and ffprobe.exe into " + binFolder + "...");
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
                    "Your clipboard is empty. Copy a video, audio, or playlist link first, then click 'Paste Link' or press Ctrl+V.",
                    "No Link in Clipboard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string rawInput = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(rawInput)) return;

            List<string> extractedUrls = DownloadUrlValidator.ExtractUrls(rawInput);
            if (extractedUrls.Count == 0)
            {
                MessageBox.Show(
                    "Please copy a valid video, audio, or playlist link starting with http:// or https://, then try again.",
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

            // Single link that is a playlist
            if (extractedUrls.Count == 1 && DownloadUrlValidator.IsPlaylistUrl(extractedUrls[0]))
            {
                using (PlaylistPromptDialog dlg = new PlaylistPromptDialog(extractedUrls[0], ytDlpPath))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        QueueUrls(dlg.ResultUrls, dlg.ResultTitles);
                    }
                }
                return;
            }

            // Multiple links (Batch Paste)
            if (extractedUrls.Count > 1)
            {
                List<string> sanitizedBatch = new List<string>();
                foreach (string u in extractedUrls)
                {
                    string singleUrl;
                    string videoId;
                    if (DownloadUrlValidator.TryExtractSingleVideoUrl(u, out singleUrl, out videoId)
                        && u.IndexOf("list=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sanitizedBatch.Add(singleUrl);
                    }
                    else
                    {
                        sanitizedBatch.Add(u);
                    }
                }
                QueueUrls(sanitizedBatch);
            }
            else
            {
                QueueUrls(extractedUrls);
            }
        }

        private void QueueUrls(IList<string> urls, IList<string> preResolvedTitles = null)
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
            bool isVideoMode = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            if (isVideoMode && (settings.EmbedSubtitles || settings.IncludeAutoCaptions))
            {
                badgeLabel += "   \u00B7   SUBS";
            }

            List<DownloadQueueItem> newItems = new List<DownloadQueueItem>();
            for (int i = 0; i < urls.Count; i++)
            {
                string u = urls[i];
                if (string.IsNullOrWhiteSpace(u)) continue;

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

            if (newItems.Count == 0) return;

            downloadManager.AddItems(newItems);

            queueListView.InsertItemsAtTop(newItems);
            UpdateQueueHeaderStats();

            if (newItems.Count == 1)
            {
                AppendLog(string.Format("[Queue] Pasted & Added: {0} [{1}] -> {2}", newItems[0].Url, newItems[0].BadgeLabel, outFolder));
            }
            else
            {
                AppendLog(string.Format("[Batch] Detected and queued {0} links from clipboard -> {1}", newItems.Count, outFolder));
                for (int i = 0; i < newItems.Count; i++)
                {
                    AppendLog(string.Format("  [{0}/{1}] {2}", i + 1, newItems.Count, newItems[i].Title != newItems[i].Url ? newItems[i].Title : newItems[i].Url));
                }
            }

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
            downloadManager.ProcessQueue(ytDlpPath, ffmpegPath, settings.ParallelCount);
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

        private void TogglePauseResumeItem(DownloadQueueItem item)
        {
            downloadManager.TogglePauseResume(item);
        }

        private void CancelSingleItem(DownloadQueueItem item)
        {
            downloadManager.Cancel(item);
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
                if (downloadManager.RemoveFinished(item))
                {
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

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || shutdownComplete) return;
            e.Cancel = true;
            if (!Enabled) return;
            Enabled = false;
            settings.Save();
            await downloadManager.ShutdownAsync();
            shutdownComplete = true;
            Close();
        }
    }
}