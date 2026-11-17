using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Stash
{
    public sealed class AppInformationDialog : ThemedForm
    {
        public UpdateInfo SelectedUpdate { get; private set; }

        // Set when the user asks to update the download engines from this dialog.
        public bool EngineUpdateRequested { get; private set; }

        public AppInformationDialog(UpdateService service) : this(service.CheckAsync, null) { }

        internal AppInformationDialog(UpdateService service, string engineStatus) : this(service.CheckAsync, engineStatus) { }

        internal AppInformationDialog(Func<Task<UpdateInfo>> checkForUpdates) : this(checkForUpdates, null) { }

        internal AppInformationDialog(Func<Task<UpdateInfo>> checkForUpdates, string engineStatus)
        {
            Text = "About Stash";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Font = Theme.GetFont("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { AppLog.Error("Could not load the dialog icon", ex); }

            int width = Dpi.Px(500);
            int inset = Dpi.Px(24);
            int contentWidth = width - inset * 2;

            Label title = Theme.CreateDialogTitle("Stash");
            title.Font = Theme.GetFont("Segoe UI Semibold", 18F);
            title.Location = new Point(inset - Dpi.Px(2), Dpi.Px(18));
            Label version = Theme.CreateDialogText("Version " + UpdateService.InstalledVersion, contentWidth);
            version.Location = new Point(inset, Dpi.Px(56));

            IconButton repository = new IconButton(IconKind.Star, "Star Stash on GitHub")
            {
                Location = new Point(width - inset - Dpi.Px(30), Dpi.Px(24)),
                IconColor = Color.FromArgb(250, 204, 21),
                HoverIconColor = Color.FromArgb(253, 224, 71)
            };
            new ToolTip().SetToolTip(repository, "Star Stash on GitHub");
            repository.Click += (s, e) => OpenLink("https://github.com/thatveejaydev/Stash");

            Label updatesHeading = CreateSectionHeading("Updates", new Point(inset, Dpi.Px(96)));
            Label status = Theme.CreateDialogText("Check for the latest version whenever you're ready.", contentWidth);
            status.Name = "UpdateStatus";
            status.Location = new Point(inset, Dpi.Px(120));

            int y = Dpi.Px(164);
            Controls.Add(title); Controls.Add(version); Controls.Add(repository);
            Controls.Add(updatesHeading); Controls.Add(status);

            if (engineStatus != null)
            {
                Controls.Add(CreateSectionHeading("Download engines", new Point(inset, y)));
                Label engines = Theme.CreateDialogText(engineStatus, contentWidth - Dpi.Px(140));
                engines.Location = new Point(inset, y + Dpi.Px(24));
                ModernButton updateEngines = Theme.CreateButton("Update engines", false, 120);
                updateEngines.Location = new Point(width - inset - updateEngines.Width, y + Dpi.Px(18));
                updateEngines.Click += (s, e) =>
                {
                    EngineUpdateRequested = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                Controls.Add(engines); Controls.Add(updateEngines);
                y += Dpi.Px(76);
            }

            ModernButton close = Theme.CreateButton("Close", false, 80);
            close.DialogResult = DialogResult.Cancel;
            ModernButton release = Theme.CreateButton("View release", false);
            ModernButton check = Theme.CreateButton("Check for updates", true, 150);
            check.Name = "CheckForUpdates";
            ModernButton install = Theme.CreateButton("Update and restart", true, 150);
            install.Name = "InstallUpdate";
            install.DialogResult = DialogResult.OK;
            install.Visible = false;
            release.Click += (s, e) =>
            {
                OpenLink(SelectedUpdate != null && !string.IsNullOrWhiteSpace(SelectedUpdate.ReleaseUrl)
                    ? SelectedUpdate.ReleaseUrl : "https://github.com/thatveejaydev/Stash/releases/tag/v" + UpdateService.InstalledVersion);
            };
            check.Click += async (s, e) =>
            {
                if (!check.Enabled) return;
                check.Enabled = false; check.Text = "Checking…";
                status.Text = "Checking for the latest stable version…";
                try
                {
                    UpdateInfo update = await checkForUpdates();
                    if (IsDisposed) return;
                    SelectedUpdate = update;
                    status.Text = update == null ? "You're up to date. Stash v" + UpdateService.InstalledVersion + " is installed."
                        : "Stash v" + update.Version + " is available. View the release notes, or update and restart.";
                    check.Visible = update == null;
                    install.Visible = update != null;
                    AcceptButton = update == null ? check : install;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Update check failed", ex);
                    if (!IsDisposed) status.Text = "Couldn't check for updates. Try again later, or view the release on GitHub.";
                }
                finally
                {
                    if (!IsDisposed) { check.Enabled = true; check.Text = "Check for updates"; }
                }
            };

            Controls.Add(Theme.CreateDialogFooter(close, release, check, install));
            ClientSize = new Size(width, y + Dpi.Px(62) + Dpi.Px(8));
            AcceptButton = check; CancelButton = close;
        }

        private static Label CreateSectionHeading(string text, Point location)
        {
            return new Label
            {
                Text = text, AutoSize = true, Location = location, UseMnemonic = false,
                Font = Theme.GetFont("Segoe UI Semibold", 9.5F), ForeColor = Theme.TextPrimary
            };
        }

        private static void OpenLink(string url)
        {
            try { Process.Start(url); }
            catch (Exception ex) { AppLog.Error("Could not open link", ex); }
        }
    }
}
