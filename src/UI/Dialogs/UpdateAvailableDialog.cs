using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MediaDownloader
{
    public sealed class UpdateAvailableDialog : Form
    {
        public UpdateAvailableDialog(UpdateInfo update)
        {
            Text = "Stash Update Available";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Dpi.Px(560), Dpi.Px(430));
            MinimumSize = new Size(Dpi.Px(460), Dpi.Px(340));
            BackColor = Theme.BgWindow; ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F); ShowInTaskbar = false;

            Label heading = new Label
            {
                Dock = DockStyle.Top, Height = Dpi.Px(58), Padding = new Padding(Dpi.Px(18), Dpi.Px(16), 0, 0),
                Text = "Stash v" + update.Version + " is available", ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold)
            };
            TextBox notes = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.IsNullOrWhiteSpace(update.ReleaseNotes) ? "No release notes were provided." : update.ReleaseNotes,
                BackColor = Theme.BgSurface, ForeColor = Theme.TextSecondary, BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(Dpi.Px(18))
            };
            Panel notesHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Dpi.Px(18), 0, Dpi.Px(18), Dpi.Px(12)) };
            notesHost.Controls.Add(notes);

            FlowLayoutPanel actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = Dpi.Px(58), FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(Dpi.Px(12), Dpi.Px(10), Dpi.Px(12), Dpi.Px(10)), BackColor = Theme.BgTopBar
            };
            Button updateButton = new Button { Text = "Update and Restart", DialogResult = DialogResult.OK, AutoSize = true, Padding = new Padding(Dpi.Px(8), Dpi.Px(3), Dpi.Px(8), Dpi.Px(3)) };
            Button laterButton = new Button { Text = "Later", DialogResult = DialogResult.Cancel, AutoSize = true, Padding = new Padding(Dpi.Px(8), Dpi.Px(3), Dpi.Px(8), Dpi.Px(3)) };
            Button releaseButton = new Button { Text = "View Release", AutoSize = true, Padding = new Padding(Dpi.Px(8), Dpi.Px(3), Dpi.Px(8), Dpi.Px(3)) };
            releaseButton.Click += (s, e) => { try { Process.Start(update.ReleaseUrl); } catch { } };
            actions.Controls.Add(updateButton); actions.Controls.Add(laterButton); actions.Controls.Add(releaseButton);
            AcceptButton = updateButton; CancelButton = laterButton;
            Controls.Add(notesHost); Controls.Add(heading); Controls.Add(actions);
        }
    }
}
