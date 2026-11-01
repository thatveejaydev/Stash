using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
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
                using (AppInformationDialog dialog = new AppInformationDialog(updateService))
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedUpdate != null)
                        await DownloadAndApplyUpdateAsync(dialog.SelectedUpdate);
                }
            }
            finally
            {
                informationDialogOpen = false;
            }
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
                if (open == DialogResult.Yes) try { Process.Start(update.ReleaseUrl); } catch { }
                return;
            }
            applyingUpdate = true;
            Enabled = false;
            try
            {
                string downloaded = await updateService.DownloadAndVerifyAsync(update);
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
            return downloadManager.GetItems().Exists(item => item.Status == QueueItemStatus.Downloading ||
                item.Status == QueueItemStatus.Merging || item.Status == QueueItemStatus.FetchingInfo);
        }
    }
}
