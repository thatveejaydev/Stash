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
            Text = "Quit Stash";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            Font = Theme.GetFont("Segoe UI", 9.5F);

            int width = Dpi.Px(520);
            int inset = Dpi.Px(24);
            Label heading = Theme.CreateDialogTitle(activeCount == 1 ? "1 download is still running" : activeCount + " downloads are still running");
            heading.Location = new Point(inset, Dpi.Px(20));
            Label body = Theme.CreateDialogText(
                "Pause to continue where you left off the next time you open Stash, or cancel and delete the partial files.",
                width - inset * 2);
            body.Location = new Point(inset, Dpi.Px(54));

            ModernButton stay = Theme.CreateButton("Keep downloading", false);
            stay.DialogResult = DialogResult.Cancel;
            ModernButton discard = Theme.CreateButton("Cancel downloads", false);
            discard.ForeColor = Theme.DangerText;
            discard.DialogResult = DialogResult.Abort;
            ModernButton pause = Theme.CreateButton("Pause and quit", true, 120);
            pause.DialogResult = DialogResult.OK;

            Controls.Add(heading); Controls.Add(body);
            Controls.Add(Theme.CreateDialogFooter(stay, discard, pause));
            ClientSize = new Size(width, body.Bottom + Dpi.Px(24) + Dpi.Px(62));
            AcceptButton = pause; CancelButton = stay;
            Shown += (s, e) => pause.Focus();
        }
    }
}
