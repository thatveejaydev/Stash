using System.Drawing;
using System.Windows.Forms;

namespace Stash
{
    // Asked when the window closes while downloads are running.
    // OK = pause and resume next time, Abort = cancel and delete partial files, Cancel = keep downloading.
    internal sealed class CloseConfirmationDialog : ThemedForm
    {
        public CloseConfirmationDialog(int activeCount, int unfinishedCount)
        {
            Text = "Downloads in progress";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.Px(520), Dpi.Px(196));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Font = Theme.GetFont("Segoe UI", 9.5F);

            Label heading = new Label
            {
                Text = activeCount == 1 ? "1 download is still in progress" : activeCount + " downloads are still in progress",
                AutoSize = true, Location = new Point(Dpi.Px(22), Dpi.Px(20)),
                Font = Theme.GetFont("Segoe UI Semibold", 12F, FontStyle.Bold), ForeColor = Theme.TextPrimary, UseMnemonic = false
            };
            Label body = new Label
            {
                Text = "Pause them to continue where they left off the next time you open Stash, or cancel them and delete their partial files.",
                Location = new Point(Dpi.Px(22), Dpi.Px(54)), Size = new Size(Dpi.Px(476), Dpi.Px(48)),
                ForeColor = Theme.TextSecondary, UseMnemonic = false
            };

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = Dpi.Px(62), BackColor = Theme.BgTopBar };
            ModernButton pause = CreateButton("Pause && Quit", 128, true);
            pause.Location = new Point(Dpi.Px(372), Dpi.Px(13));
            pause.DialogResult = DialogResult.OK;
            ModernButton discard = CreateButton("Cancel Downloads", 140, false);
            discard.Location = new Point(Dpi.Px(224), Dpi.Px(13));
            discard.ForeColor = Color.FromArgb(252, 165, 165);
            discard.DialogResult = DialogResult.Abort;
            ModernButton stay = CreateButton("Keep Downloading", 140, false);
            stay.Location = new Point(Dpi.Px(18), Dpi.Px(13));
            stay.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(pause); footer.Controls.Add(discard); footer.Controls.Add(stay);

            Controls.Add(heading); Controls.Add(body); Controls.Add(footer);
            AcceptButton = pause; CancelButton = stay;
            Shown += (s, e) => pause.Focus();
        }

        private static ModernButton CreateButton(string text, int width, bool primary)
        {
            return new ModernButton
            {
                Text = text, Size = new Size(Dpi.Px(width), Dpi.Px(36)), Font = Theme.GetFont("Segoe UI Semibold", 9F),
                CornerRadius = Dpi.Px(8), BaseColor = primary ? Theme.BrandPurple : Theme.BgSurface,
                HoverColor = primary ? Theme.BrandPurpleHover : Theme.BgSurfaceHover,
                BorderColor = primary ? Color.Empty : Theme.BorderSubtle
            };
        }
    }
}
