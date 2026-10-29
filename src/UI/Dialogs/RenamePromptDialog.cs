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
            Text = "Stash";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.Px(690), Dpi.Px(354));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.BgTopBar;
            ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            Padding = new Padding(Dpi.Px(18));

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
                Text = "File already exists",
                Location = new Point(Dpi.Px(72), Dpi.Px(22)),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary
            };

            Label lblExisting = new Label
            {
                Text = existingFileName,
                Location = new Point(Dpi.Px(12), Dpi.Px(37)),
                Size = new Size(Dpi.Px(626), Dpi.Px(22)),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Theme.TextPrimary,
                BackColor = Theme.BgSurface,
                UseMnemonic = false
            };

            Label lblPrompt = new Label
            {
                Text = "Save as",
                Location = new Point(Dpi.Px(20), Dpi.Px(168)),
                AutoSize = true,
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
            };

            CardPanel pnlInput = new CardPanel
            {
                Location = new Point(Dpi.Px(20), Dpi.Px(194)),
                Size = new Size(Dpi.Px(650), Dpi.Px(42)),
                Padding = new Padding(Dpi.Px(12), Dpi.Px(11), Dpi.Px(12), Dpi.Px(8)),
                CardFillColor = Theme.BgInput,
                CardBorderColor = Theme.BorderFocus,
                CornerRadius = Dpi.Px(8)
            };

            txtNewName = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BgInput,
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI", 10F),
                Text = suggestedNewBaseName,
                AccessibleName = "Save as filename"
            };
            pnlInput.Controls.Add(txtNewName);

            ModernButton btnCancel = new ModernButton
            {
                Text = "Cancel",
                Location = new Point(Dpi.Px(451), Dpi.Px(301)),
                Size = new Size(Dpi.Px(70), Dpi.Px(40)),
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
                Text = "Save as New File",
                Location = new Point(Dpi.Px(529), Dpi.Px(301)),
                Size = new Size(Dpi.Px(141), Dpi.Px(40)),
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

            Panel warning = new Panel { Location = new Point(Dpi.Px(20), Dpi.Px(20)), Size = new Size(Dpi.Px(40), Dpi.Px(40)) };
            warning.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = Theme.CreateRoundedRectangle(new Rectangle(0, 0, Dpi.Px(39), Dpi.Px(39)), 11))
                using (var fill = new SolidBrush(Color.FromArgb(62, 48, 27)))
                using (var icon = new Pen(Theme.AccentWarning, 1.5f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawLines(icon, new Point[] { new Point(Dpi.Px(14), Dpi.Px(12)), new Point(Dpi.Px(23), Dpi.Px(12)), new Point(Dpi.Px(27), Dpi.Px(16)), new Point(Dpi.Px(27), Dpi.Px(28)), new Point(Dpi.Px(14), Dpi.Px(28)), new Point(Dpi.Px(14), Dpi.Px(12)) });
                    e.Graphics.DrawLine(icon, Dpi.Px(20), Dpi.Px(17), Dpi.Px(20), Dpi.Px(22));
                    e.Graphics.DrawEllipse(icon, Dpi.Px(19.5f), Dpi.Px(25), Dpi.Px(1), Dpi.Px(1));
                }
            };
            Controls.Add(warning);
            Controls.Add(lblTitle);
            Controls.Add(new Label
            {
                Text = "A file with a similar name was found in your download folder.",
                Location = new Point(Dpi.Px(72), Dpi.Px(48)), Size = new Size(Dpi.Px(590), Dpi.Px(22)),
                ForeColor = Theme.TextSecondary, Font = new Font("Segoe UI", 9F)
            });
            CardPanel existing = new CardPanel
            {
                Location = new Point(Dpi.Px(20), Dpi.Px(82)), Size = new Size(Dpi.Px(650), Dpi.Px(70)),
                CardFillColor = Theme.BgSurface, CardBorderColor = Color.Empty, CornerRadius = Dpi.Px(12)
            };
            existing.Controls.Add(new Label
            {
                Text = "EXISTING FILE", Location = new Point(Dpi.Px(12), Dpi.Px(12)), Size = new Size(Dpi.Px(620), Dpi.Px(18)),
                Font = new Font("Segoe UI", 8F), ForeColor = Theme.TextSecondary, BackColor = Theme.BgSurface
            });
            existing.Controls.Add(lblExisting);
            Controls.Add(existing);
            Controls.Add(new Label
            {
                Text = "The " + extension + " extension will be added automatically.",
                Location = new Point(Dpi.Px(20), Dpi.Px(242)), Size = new Size(Dpi.Px(650), Dpi.Px(22)),
                ForeColor = Theme.TextMuted, Font = new Font("Segoe UI", 8.5F)
            });
            Controls.Add(new Panel { Location = new Point(Dpi.Px(20), Dpi.Px(278)), Size = new Size(Dpi.Px(650), Dpi.Px(1)), BackColor = Theme.BorderSubtle });
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
