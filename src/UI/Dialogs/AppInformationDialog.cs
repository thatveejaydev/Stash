using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
{
    public sealed class AppInformationDialog : Form
    {
        private const int WsExComposited = 0x02000000;
        private const int DwmTransitionsForceDisabled = 3;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        public UpdateInfo SelectedUpdate { get; private set; }

        public AppInformationDialog(UpdateService service) : this(service.CheckAsync) { }

        internal AppInformationDialog(Func<Task<UpdateInfo>> checkForUpdates)
        {
            Text = "About Stash";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.Px(530), Dpi.Px(280));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            BackColor = Theme.BgWindow; ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Label title = new Label { Text = "Stash", AutoSize = true, Location = new Point(Dpi.Px(24), Dpi.Px(20)),
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold) };
            Label version = new Label { Text = "Version " + UpdateService.InstalledVersion,
                AutoSize = true, Location = new Point(Dpi.Px(26), Dpi.Px(64)), ForeColor = Theme.TextMuted };
            Label starPrompt = new Label { Text = "Give a star on GitHub", AutoSize = false,
                Location = new Point(Dpi.Px(284), Dpi.Px(45)), Size = new Size(Dpi.Px(180), Dpi.Px(34)),
                TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.TextMuted,
                Font = new Font("Segoe UI", 9F) };
            ModernButton repository = new StarIconButton
            {
                BaseColor = Theme.BgTopBar, HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle, CornerRadius = Dpi.Px(8)
            };
            repository.Size = new Size(Dpi.Px(34), Dpi.Px(34));
            repository.Location = new Point(Dpi.Px(472), Dpi.Px(45));
            repository.ForeColor = Color.FromArgb(250, 204, 21);
            repository.AccessibleName = "Star Stash on GitHub";
            repository.Click += (s, e) =>
            {
                try { Process.Start("https://github.com/thatveejaydev/Stash"); }
                catch { }
            };
            CardPanel card = new CardPanel { Location = new Point(Dpi.Px(24), Dpi.Px(100)),
                Size = new Size(Dpi.Px(482), Dpi.Px(96)), Padding = new Padding(Dpi.Px(16)) };
            Label status = new Label { Name = "UpdateStatus", Dock = DockStyle.Fill,
                Text = "Check for the latest version whenever you're ready.", ForeColor = Theme.TextSecondary,
                AutoEllipsis = true, UseMnemonic = false };
            card.Controls.Add(status);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Dpi.Px(64),
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                Padding = new Padding(Dpi.Px(16), Dpi.Px(14), Dpi.Px(16), 0), BackColor = Theme.BgTopBar };
            ModernButton close = CreateButton("Close", 70, false);
            close.DialogResult = DialogResult.Cancel;
            ModernButton check = CreateButton("Check for Updates", 150, true);
            check.Name = "CheckForUpdates";
            ModernButton install = CreateButton("Update and Restart", 164, true);
            install.Name = "InstallUpdate";
            install.DialogResult = DialogResult.OK;
            install.Visible = false;
            ModernButton release = CreateButton("View Release", 112, false);
            release.Click += (s, e) =>
            {
                string url = SelectedUpdate != null && !string.IsNullOrWhiteSpace(SelectedUpdate.ReleaseUrl)
                    ? SelectedUpdate.ReleaseUrl : "https://github.com/thatveejaydev/Stash/releases/tag/v" + UpdateService.InstalledVersion;
                try { Process.Start(url); } catch { }
            };
            check.Click += async (s, e) =>
            {
                if (!check.Enabled) return;
                check.Enabled = false; check.Text = "Checking...";
                status.Text = "Checking for the latest stable version...";
                try
                {
                    UpdateInfo update = await checkForUpdates();
                    if (IsDisposed) return;
                    SelectedUpdate = update;
                    status.Text = update == null ? "You're up to date.\r\nStash v" + UpdateService.InstalledVersion + " is installed."
                        : "Stash v" + update.Version + " is available.\r\nView the release on GitHub or update and restart.";
                    check.Visible = update == null;
                    install.Visible = update != null;
                    AcceptButton = update == null ? check : install;
                }
                catch (Exception)
                {
                    if (!IsDisposed) status.Text = "Couldn't check for updates. Try again later.\r\nYou can also view the release on GitHub.";
                }
                finally
                {
                    if (!IsDisposed) { check.Enabled = true; check.Text = "Check for Updates"; }
                }
            };
            actions.Controls.Add(close); actions.Controls.Add(install); actions.Controls.Add(check); actions.Controls.Add(release);
            Controls.Add(card); Controls.Add(version); Controls.Add(title); Controls.Add(starPrompt); Controls.Add(repository); Controls.Add(actions);
            AcceptButton = check; CancelButton = close;
        }

        private static ModernButton CreateButton(string text, int width, bool primary)
        {
            return new ModernButton { Text = text, Size = new Size(Dpi.Px(width), Dpi.Px(36)),
                Margin = new Padding(Dpi.Px(8), 0, 0, 0), Font = new Font("Segoe UI Semibold", 9F), CornerRadius = Dpi.Px(8),
                BaseColor = primary ? Theme.BrandPurple : Theme.BgTopBar,
                HoverColor = primary ? Theme.BrandPurpleHover : Theme.BgSurfaceHover,
                BorderColor = primary ? Color.Empty : Theme.BorderSubtle };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // Present the dialog and its child controls in one buffered paint.
                parameters.ExStyle |= WsExComposited;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int disableTransitions = 1;
                DwmSetWindowAttribute(Handle, DwmTransitionsForceDisabled, ref disableTransitions, sizeof(int));
                int dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
