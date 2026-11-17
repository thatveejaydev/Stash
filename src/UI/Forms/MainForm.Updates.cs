using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private readonly UpdateService updateService = new UpdateService(AppPaths.Current.UpdatesDirectory);
        private bool informationDialogOpen;
        private bool applyingUpdate;

        private async Task ShowInformationAsync()
        {
            if (informationDialogOpen || applyingUpdate) return;
            informationDialogOpen = true;
            try
            {
                using (AppInformationDialog dialog = new AppInformationDialog(updateService, GetEngineStatusText()))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedUpdate != null)
                        await DownloadAndApplyUpdateAsync(dialog.SelectedUpdate);
                    else if (dialog.EngineUpdateRequested)
                        RunEngineOperationAsync(false);
                }
            }
            finally
            {
                informationDialogOpen = false;
            }
        }

        private string GetEngineStatusText()
        {
            if (engineOperationRunning) return "The engines are being updated now.";
            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath)) return "yt-dlp or FFmpeg is missing. Install them to download.";
            DateTime checkedAt = settings.LastEngineCheckUtc;
            return checkedAt == DateTime.MinValue || checkedAt.Year < 2000
                ? "yt-dlp and FFmpeg are installed."
                : "yt-dlp and FFmpeg are installed. Last checked " + checkedAt.ToLocalTime().ToString("d MMM yyyy") + ".";
        }

        private async Task DownloadAndApplyUpdateAsync(UpdateInfo update)
        {
            if (HasActiveDownloads())
            {
                MessageBox.Show(this, "Finish or cancel active downloads before updating Stash.",
                    "Downloads in Progress", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string target = Application.ExecutablePath;
            if (!UpdateService.CanReplaceExecutable(target))
            {
                DialogResult open = MessageBox.Show(this,
                    "Stash cannot update its current folder because it is not writable. Open the GitHub release page to update manually?",
                    "Manual Update Required", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (open == DialogResult.Yes) try { Process.Start(update.ReleaseUrl); } catch (Exception ex) { AppLog.Error("Could not open link", ex); }
                return;
            }
            applyingUpdate = true;
            Enabled = false;
            try
            {
                string downloaded = await updateService.DownloadAndVerifyAsync(update);
                SaveQueue();
                updateService.CreateAndLaunchHelper(downloaded, target, Process.GetCurrentProcess().Id);
                shutdownComplete = true;
                Close();
            }
            catch (Exception ex)
            {
                applyingUpdate = false; Enabled = true;
                MessageBox.Show(this, "Stash could not download or prepare the update.\n\n" + ex.Message,
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool HasActiveDownloads()
        {
            if (downloadManager == null) return false;
            return downloadManager.GetItems().Exists(item => DownloadManager.IsActive(item.Status));
        }
    }
}
