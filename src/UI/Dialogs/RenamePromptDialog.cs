using System;
using System.Drawing;
using System.Windows.Forms;

namespace Stash
{
    // Asked before a download would create a file with the same name as an existing one.
    public class RenamePromptDialog : ThemedForm
    {
        private readonly TextBox txtNewName;
        public string ChosenBaseName { get; private set; }

        public RenamePromptDialog(string existingFileName, string suggestedNewBaseName, string extension)
        {
            Text = "File already exists";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = Theme.GetFont("Segoe UI", 9.5F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception ex) { AppLog.Error("Could not load the dialog icon", ex); }

            int width = Dpi.Px(560);
            int inset = Dpi.Px(24);
            int contentWidth = width - inset * 2;

            Label title = Theme.CreateDialogTitle("A file with this name already exists");
            title.Location = new Point(inset, Dpi.Px(20));
            Label body = Theme.CreateDialogText("Save the new download under a different name, or cancel it.", contentWidth);
            body.Location = new Point(inset, Dpi.Px(52));

            CardPanel existing = new CardPanel
            {
                Location = new Point(inset, Dpi.Px(86)), Size = new Size(contentWidth, Dpi.Px(56)),
                CardFillColor = Theme.BgSurface, CardBorderColor = Theme.BorderSubtle, CornerRadius = Dpi.Px(10)
            };
            existing.Controls.Add(new Label
            {
                Text = "Existing file", Location = new Point(Dpi.Px(14), Dpi.Px(8)), AutoSize = true,
                Font = Theme.GetFont("Segoe UI", 8.5F), ForeColor = Theme.TextMuted, BackColor = Theme.BgSurface
            });
            existing.Controls.Add(new Label
            {
                Text = existingFileName, Location = new Point(Dpi.Px(14), Dpi.Px(27)), Size = new Size(contentWidth - Dpi.Px(28), Dpi.Px(20)),
                AutoEllipsis = true, Font = Theme.GetFont("Segoe UI", 9.5F), ForeColor = Theme.TextPrimary, BackColor = Theme.BgSurface, UseMnemonic = false
            });

            Label saveAs = new Label
            {
                Text = "Save as", Location = new Point(inset, Dpi.Px(158)), AutoSize = true,
                Font = Theme.GetFont("Segoe UI Semibold", 9.5F), ForeColor = Theme.TextPrimary
            };
            CardPanel inputCard = new CardPanel
            {
                Location = new Point(inset, Dpi.Px(182)), Size = new Size(contentWidth, Dpi.Px(40)),
                Padding = new Padding(Dpi.Px(12), Dpi.Px(10), Dpi.Px(12), Dpi.Px(6)),
                CardFillColor = Theme.BgInput, CardBorderColor = Theme.BorderFocus, CornerRadius = Dpi.Px(10)
            };
            txtNewName = new TextBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.BgInput, ForeColor = Theme.TextPrimary,
                Font = Theme.GetFont("Segoe UI", 10F), Text = suggestedNewBaseName, AccessibleName = "New file name"
            };
            inputCard.Controls.Add(txtNewName);
            Label note = Theme.CreateDialogText(extension + " is added automatically.", contentWidth);
            note.ForeColor = Theme.TextMuted;
            note.Font = Theme.GetFont("Segoe UI", 8.5F);
            note.Location = new Point(inset, Dpi.Px(228));
            Label error = new Label
            {
                AutoSize = true, Location = new Point(inset, Dpi.Px(228)), ForeColor = Theme.DangerText,
                Font = Theme.GetFont("Segoe UI", 8.5F), Visible = false, Text = "Enter a file name."
            };

            ModernButton cancel = Theme.CreateButton("Cancel", false, 80);
            cancel.DialogResult = DialogResult.Cancel;
            ModernButton save = Theme.CreateButton("Save as new file", true, 130);
            save.Click += (s, e) =>
            {
                string cleaned = SanitizeFileName(txtNewName.Text.Trim());
                if (string.IsNullOrEmpty(cleaned))
                {
                    note.Visible = false;
                    error.Visible = true;
                    txtNewName.Focus();
                    return;
                }
                ChosenBaseName = cleaned;
                DialogResult = DialogResult.OK;
                Close();
            };
            txtNewName.TextChanged += (s, e) => { error.Visible = false; note.Visible = true; };

            Controls.Add(title); Controls.Add(body); Controls.Add(existing); Controls.Add(saveAs);
            Controls.Add(inputCard); Controls.Add(note); Controls.Add(error);
            Controls.Add(Theme.CreateDialogFooter(cancel, save));
            ClientSize = new Size(width, Dpi.Px(262) + Dpi.Px(62));
            AcceptButton = save;
            CancelButton = cancel;
            Shown += (s, e) =>
            {
                txtNewName.Focus();
                txtNewName.SelectAll();
            };
        }

        public static string SanitizeFileName(string name)
        {
            return StorageFileService.SanitizeFileName(name);
        }
    }
}
