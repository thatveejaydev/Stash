using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private static readonly TimeSpan EngineCheckInterval = TimeSpan.FromDays(1);
        private static readonly TimeSpan FfmpegRefreshAge = TimeSpan.FromDays(30);

        private void CheckToolsStatus()
        {
            ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
            ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");
            if (queueListView != null) queueListView.ThumbnailDecoderPath = ffmpegPath;

            bool hasYtDlp = File.Exists(ytDlpPath);
            bool hasFfmpeg = File.Exists(ffmpegPath);

            // The footer stays quiet while engines are fine; it only speaks up when action is needed.
            if (hasYtDlp && hasFfmpeg)
            {
                lblToolsStatus.Visible = false;
            }
            else
            {
                List<string> missing = new List<string>();
                if (!hasYtDlp) missing.Add("yt-dlp");
                if (!hasFfmpeg) missing.Add("FFmpeg");
                lblToolsStatus.Text = "Install " + string.Join(" and ", missing) + " to start downloading";
                lblToolsStatus.ForeColor = Theme.AccentWarning;
                lblToolsStatus.Visible = true;
                appToolTip.SetToolTip(lblToolsStatus, "Click to install the download engines.");
            }
        }

        private void BtnInstallTools_Click(object sender, EventArgs e)
        {
            RunEngineOperationAsync(false);
        }

        // Daily background check, only when nothing is downloading.
        private void ScheduleAutomaticEngineCheck()
        {
            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath)) return;
            if (DateTime.UtcNow - settings.LastEngineCheckUtc < EngineCheckInterval) return;
            if (HasActiveDownloads()) return;
            RunEngineOperationAsync(true);
        }

        private async void RunEngineOperationAsync(bool automatic)
        {
            if (engineOperationRunning || IsDisposed) return;
            if (HasActiveDownloads())
            {
                if (!automatic)
                    MessageBox.Show(this, "Pause or finish active downloads before updating the download engines.",
                        "Downloads in Progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            engineOperationRunning = true;
            lblToolsStatus.ForeColor = Theme.TextSecondary;
            lblToolsStatus.Text = "Preparing download engines…";
            lblToolsStatus.Visible = true;
            lblToolsStatus.Cursor = Cursors.WaitCursor;
            appToolTip.SetToolTip(lblToolsStatus, "Preparing download engines...");
            EngineInstaller installer = new EngineInstaller(binFolder);
            EngineInstaller.ProgressReporter progress = (status, percent) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() => { if (!IsDisposed) lblToolsStatus.Text = status; }));
            };
            bool installedSomething = false;
            try
            {
                if (!File.Exists(installer.YtDlpPath))
                {
                    AppendLog("Installing yt-dlp from GitHub releases...");
                    await installer.InstallYtDlpAsync(progress, engineCancellation.Token);
                    installedSomething = true;
                    AppendLog("yt-dlp.exe installed to " + installer.YtDlpPath);
                }
                else
                {
                    lblToolsStatus.Text = "Updating yt-dlp…";
                    Tuple<int, string> result = await EngineInstaller.SelfUpdateYtDlpAsync(installer.YtDlpPath);
                    if (result.Item1 != 0) logWriter.WriteError("yt-dlp update failed with exit code " + result.Item1 + ": " + result.Item2);
                    else if (!string.IsNullOrWhiteSpace(result.Item2)) AppendLog(result.Item2);
                }

                bool ffmpegMissing = !File.Exists(installer.FfmpegPath) || !File.Exists(installer.FfprobePath);
                bool ffmpegStale = !ffmpegMissing && !automatic &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(installer.FfmpegPath) > FfmpegRefreshAge;
                if (ffmpegMissing || ffmpegStale)
                {
                    AppendLog(ffmpegMissing ? "Installing FFmpeg (yt-dlp/FFmpeg-Builds)..." : "Refreshing FFmpeg (older than 30 days)...");
                    await installer.InstallFfmpegAsync(progress, engineCancellation.Token);
                    installedSomething = true;
                    AppendLog("FFmpeg ready at " + binFolder);
                }

                settings.LastEngineCheckUtc = DateTime.UtcNow;
                settings.Save();
                if (installedSomething) ShowNotice("Download engines installed.");
                else if (!automatic) ShowNotice("Download engines are up to date.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logWriter.WriteError("Error installing tools: " + ex);
                if (!IsDisposed && !automatic)
                    MessageBox.Show(this, "Failed to prepare the download engines:\n" + ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                engineOperationRunning = false;
                if (!IsDisposed)
                {
                    lblToolsStatus.Cursor = Cursors.Hand;
                    CheckToolsStatus();
                    // Links queued while engines were being replaced start now.
                    ProcessQueue();
                }
            }
        }
    }
}
