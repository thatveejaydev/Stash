using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Modern Dark Dialog to Prompt for Renaming When a Similar Filename Exists
    public class RenamePromptDialog : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private TextBox txtNewName;
        public string ChosenBaseName { get; private set; }

        public RenamePromptDialog(string existingFileName, string suggestedNewBaseName, string extension)
        {
            Text = "Similar File Already Exists";
            Size = new Size(520, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.BgWindow;
            ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            Padding = new Padding(18);

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                }
            }
            catch { }

            Label lblTitle = new Label
            {
                Text = "A file with a similar name already exists in this folder:",
                Location = new Point(18, 16),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary
            };

            Label lblExisting = new Label
            {
                Text = existingFileName,
                Location = new Point(18, 40),
                Size = new Size(468, 22),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Theme.AccentWarning
            };

            Label lblPrompt = new Label
            {
                Text = string.Format("Enter filename for this download ({0}):", extension),
                Location = new Point(18, 72),
                AutoSize = true,
                ForeColor = Theme.TextSecondary
            };

            Panel pnlInput = new Panel
            {
                Location = new Point(18, 98),
                Size = new Size(468, 34),
                Padding = new Padding(8, 6, 8, 4),
                BackColor = Theme.BgInput
            };
            pnlInput.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderFocus, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnlInput.Width - 1, pnlInput.Height - 1);
                }
            };

            txtNewName = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BgInput,
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI", 10F),
                Text = suggestedNewBaseName
            };
            pnlInput.Controls.Add(txtNewName);

            ModernButton btnCancel = new ModernButton
            {
                Text = "Cancel Download",
                Location = new Point(226, 150),
                Size = new Size(125, 34),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 9F)
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            ModernButton btnSave = new ModernButton
            {
                Text = "Save & Download",
                Location = new Point(359, 150),
                Size = new Size(127, 34),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            btnSave.Click += (s, e) =>
            {
                string cleaned = SanitizeFileName(txtNewName.Text.Trim());
                if (string.IsNullOrEmpty(cleaned))
                {
                    txtNewName.Focus();
                    return;
                }
                ChosenBaseName = cleaned;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(lblTitle);
            Controls.Add(lblExisting);
            Controls.Add(lblPrompt);
            Controls.Add(pnlInput);
            Controls.Add(btnCancel);
            Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

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
