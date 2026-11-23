using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stash
{
    internal sealed class LinkInputBar : Control
    {
        private readonly LinkTextBox input;
        private readonly ModernButton download;
        private readonly RadioButton[] modes = new RadioButton[3];
        private readonly ChoiceButton format;
        private readonly ChoiceButton detail;
        private readonly ChoiceButton folder;
        private readonly IconButton options;
        private readonly Label status;
        private Rectangle inputBounds;
        private bool updating;

        public event Action<string> SubmitRequested;
        public event Action<DownloadKind> ModeChanged;
        public event EventHandler LinkChanged;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        public LinkInputBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgTopBar;
            input = new LinkTextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.BgInput, ForeColor = Theme.TextPrimary,
                Font = Theme.GetFont("Segoe UI", 10.5F), AccessibleName = "Link to download", TabIndex = 0
            };
            input.HandleCreated += (s, e) => SendMessage(input.Handle, 0x1501, new IntPtr(1), "Paste or drop a link");
            input.GotFocus += (s, e) => Invalidate();
            input.LostFocus += (s, e) => Invalidate();
            input.TextChanged += (s, e) => { if (LinkChanged != null) LinkChanged(this, e); };
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.Handled = e.SuppressKeyPress = true;
                Submit();
            };
            Controls.Add(input);

            download = Theme.CreateButton("Download video", true, 100);
            download.TabIndex = 1;
            download.Click += (s, e) => Submit();
            Controls.Add(download);

            string[] names = { "Video", "Audio", "Transcript" };
            for (int i = 0; i < modes.Length; i++)
            {
                DownloadKind kind = (DownloadKind)i;
                ModeSegment mode = new ModeSegment(i == 0, i == modes.Length - 1)
                {
                    Text = names[i], AccessibleName = names[i], Font = Theme.GetFont("Segoe UI", 9.5F), TabIndex = i + 2
                };
                mode.CheckedChanged += (s, e) =>
                {
                    if (!updating && mode.Checked && ModeChanged != null) ModeChanged(kind);
                };
                modes[i] = mode;
                Controls.Add(mode);
            }

            format = new ChoiceButton { AccessibleName = "Format", TabIndex = 5 };
            detail = new ChoiceButton { TabIndex = 6 };
            folder = new ChoiceButton { AccessibleName = "Save to folder", TabIndex = 7, ShowFolder = true };
            options = new IconButton(IconKind.Gear, "More options") { TabIndex = 8 };
            status = new Label
            {
                AutoEllipsis = true, UseMnemonic = false, Font = Theme.GetFont("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft, TabStop = false
            };
            Controls.AddRange(new Control[] { format, detail, folder, options, status });
        }

        public Control FormatAnchor { get { return format; } }
        public Control DetailAnchor { get { return detail; } }
        public Control FolderAnchor { get { return folder; } }
        public Control OptionsAnchor { get { return options; } }
        public IButtonControl DownloadAction { get { return download; } }
        public bool InputFocused { get { return input.Focused; } }
        public string LinkText { get { return input.Text; } }
        internal bool HasDetail { get; private set; }

        public void ShowSettings(DownloadKind kind, string first, string second, string folderName)
        {
            updating = true;
            modes[(int)kind].Checked = true;
            updating = false;

            download.Text = kind == DownloadKind.Transcript ? "Download transcript" :
                kind == DownloadKind.Audio ? "Download audio" : "Download video";
            format.Text = first;
            format.AccessibleName = kind == DownloadKind.Transcript ? "Caption language" : "Format";
            detail.Text = second ?? "";
            detail.AccessibleName = kind == DownloadKind.Transcript ? "Transcript format" : "Video quality";
            HasDetail = second != null;
            detail.Visible = HasDetail;
            folder.Text = folderName;
            PerformLayout();
        }

        public void ShowStatus(string message, Color color)
        {
            bool wasShown = status.Text.Length > 0;
            status.Text = message ?? "";
            status.ForeColor = color;
            // The status line only takes space while it has something to say.
            if (wasShown != (status.Text.Length > 0)) PerformLayout();
        }

        public void FocusInput() { input.Focus(); }
        public void Clear() { input.Clear(); }

        public void Stage(string text, bool insert)
        {
            string singleLine = LinkTextBox.SingleLine(text);
            if (insert) input.SelectedText = singleLine;
            else input.Text = singleLine;
            input.Focus();
        }

        private void Submit()
        {
            if (SubmitRequested != null) SubmitRequested(input.Text);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (status == null || Width < 1) return;
            int gap = Dpi.Px(6);
            int inputHeight = Dpi.Px(38);
            int actionWidth = TextRenderer.MeasureText(download.Text, download.Font).Width + Dpi.Px(28);
            download.SetBounds(Math.Max(1, Width - actionWidth), 0, actionWidth, inputHeight);
            inputBounds = new Rectangle(0, 0, Math.Max(1, download.Left - gap), inputHeight);
            int left = Dpi.Px(40);
            input.SetBounds(left, (inputHeight - input.PreferredHeight) / 2, Math.Max(1, inputBounds.Width - left - gap), input.PreferredHeight);

            int rowHeight = Dpi.Px(30);
            int rowTop = inputHeight + gap;
            int modeRight = 0;
            int[] modeWidths = { 66, 66, 94 };
            for (int i = 0; i < modes.Length; i++)
            {
                modes[i].SetBounds(modeRight, rowTop, Dpi.Px(modeWidths[i]), rowHeight);
                modeRight = modes[i].Right - 1;
            }

            int gearWidth = Dpi.Px(30);
            int formatWidth = format.ChoiceWidth, detailWidth = HasDetail ? detail.ChoiceWidth : 0, folderWidth = folder.ChoiceWidth;
            int settingsWidth = formatWidth + detailWidth + folderWidth + gearWidth + gap * (HasDetail ? 3 : 2);
            if (modeRight + gap + settingsWidth > Width) rowTop += rowHeight + gap;
            int optionLeft = Math.Max(1, Width - gearWidth);
            options.SetBounds(optionLeft, rowTop, gearWidth, rowHeight);
            int itemLeft = Math.Max(0, Width - settingsWidth);
            format.SetBounds(itemLeft, rowTop, formatWidth, rowHeight);
            itemLeft = format.Right + gap;
            if (HasDetail)
            {
                detail.SetBounds(itemLeft, rowTop, detailWidth, rowHeight);
                itemLeft = detail.Right + gap;
            }
            folder.SetBounds(itemLeft, rowTop, Math.Max(1, optionLeft - gap - itemLeft), rowHeight);
            bool showStatus = status.Text.Length > 0;
            status.Visible = showStatus;
            status.SetBounds(0, rowTop + rowHeight + Dpi.Px(2), Width, showStatus ? Dpi.Px(20) : 0);
            if (Height != status.Bottom) Height = status.Bottom;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (inputBounds.Contains(e.Location)) input.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (inputBounds.Width <= 1) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = Rectangle.Inflate(inputBounds, -1, -1);
            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, Dpi.Px(9)))
            using (SolidBrush fill = new SolidBrush(Theme.BgInput))
            using (Pen border = new Pen(input.Focused ? Theme.BorderFocus : Theme.BorderHover))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            Icons.Draw(g, IconKind.Link, new Rectangle(Dpi.Px(12), 0, Dpi.Px(18), inputBounds.Height), Theme.TextMuted);
        }

        private sealed class LinkTextBox : TextBox
        {
            internal static string SingleLine(string value)
            {
                return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg != 0x0302) { base.WndProc(ref message); return; }
                try
                {
                    if (Clipboard.ContainsText()) SelectedText = SingleLine(Clipboard.GetText());
                }
                catch (ExternalException) { base.WndProc(ref message); }
            }
        }

        // One part of the Video | Audio | Transcript switch. Only the outer ends are rounded,
        // so the three parts read as a single control with the same radius as the other buttons.
        private sealed class ModeSegment : RadioButton
        {
            private static readonly Color CheckedFill = Color.FromArgb(60, 43, 99);
            private readonly bool roundLeft;
            private readonly bool roundRight;
            private bool hovered;

            internal ModeSegment(bool roundLeft, bool roundRight)
            {
                this.roundLeft = roundLeft;
                this.roundRight = roundRight;
                Appearance = Appearance.Button;
                FlatStyle = FlatStyle.Flat;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
            protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Parent != null ? Parent.BackColor : Theme.BgTopBar);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                Color fill = Checked ? CheckedFill : hovered ? Theme.BgSurfaceHover : Theme.BgSurface;
                using (GraphicsPath path = Theme.CreateRoundedRectangle(bounds, Dpi.Px(7), roundLeft, roundRight))
                using (SolidBrush brush = new SolidBrush(fill))
                using (Pen border = new Pen(Focused && ShowFocusCues ? Theme.BorderFocus : Theme.BorderHover))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(border, path);
                }
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, Checked ? Theme.BrandPurpleLight : Theme.TextPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class ChoiceButton : ModernButton
        {
            internal bool ShowFolder;

            internal ChoiceButton()
            {
                Font = Theme.GetFont("Segoe UI", 9.5F);
                AccessibleRole = AccessibleRole.ButtonDropDown;
            }

            internal int ChoiceWidth
            {
                get
                {
                    int padding = Dpi.Px(ShowFolder ? 64 : 44);
                    return Math.Min(Dpi.Px(160), Math.Max(Dpi.Px(68), TextRenderer.MeasureText(Text, Font).Width + padding));
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                // Paint the area outside the rounded shape so its corners blend into the header.
                g.Clear(Parent != null ? Parent.BackColor : Theme.BgTopBar);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(bounds, Dpi.Px(7)))
                using (SolidBrush fill = new SolidBrush(ClientRectangle.Contains(PointToClient(MousePosition)) ? Theme.BgSurfaceHover : Theme.BgSurface))
                using (Pen border = new Pen(Focused ? Theme.BorderFocus : Theme.BorderHover))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                int left = Dpi.Px(ShowFolder ? 33 : 11);
                if (ShowFolder) Icons.Draw(g, IconKind.Folder, new Rectangle(Dpi.Px(10), 0, Dpi.Px(16), Height), Theme.TextSecondary);
                Rectangle text = new Rectangle(left, 0, Math.Max(1, Width - left - Dpi.Px(25)), Height);
                TextRenderer.DrawText(g, Text, Font, text, Theme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Theme.DrawChevron(g, new Point(Width - Dpi.Px(14), Height / 2), Theme.TextSecondary);
            }
        }
    }
}
