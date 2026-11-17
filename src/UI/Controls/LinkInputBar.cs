using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stash
{
    // The single entry point of the window: a link field, the download preset chip and
    // the Download button, drawn as one rounded bar.
    internal sealed class LinkInputBar : Control
    {
        private readonly TextBox input;
        private readonly PresetChip chip;
        private readonly PresetChip options;
        private readonly ModernButton download;
        private bool focused;

        public event Action<string> SubmitRequested;
        public event EventHandler PresetClicked;
        public event EventHandler OptionsClicked;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        public LinkInputBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.BgTopBar;
            Height = Dpi.Px(46);

            input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BgInput,
                ForeColor = Theme.TextPrimary,
                Font = Theme.GetFont("Segoe UI", 10.5F),
                AccessibleName = "Link to download"
            };
            input.HandleCreated += (s, e) => SendMessage(input.Handle, 0x1501, new IntPtr(1), "Paste or drop a link"); // EM_SETCUEBANNER
            input.GotFocus += (s, e) => { focused = true; Invalidate(); };
            input.LostFocus += (s, e) => { focused = false; Invalidate(); };
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.Handled = e.SuppressKeyPress = true;
                Submit();
            };

            chip = new PresetChip { AccessibleName = "Format, quality and folder" };
            chip.Click += (s, e) => { if (PresetClicked != null) PresetClicked(this, EventArgs.Empty); };
            options = new PresetChip { Text = "More options", AccessibleName = "More options" };
            options.Click += (s, e) => { if (OptionsClicked != null) OptionsClicked(this, EventArgs.Empty); };

            download = Theme.CreateButton("Download", true, 96);
            download.Margin = Padding.Empty;
            download.Click += (s, e) => Submit();

            Controls.Add(input);
            Controls.Add(chip);
            Controls.Add(options);
            Controls.Add(download);
        }

        public Control PresetAnchor { get { return chip; } }
        public Control OptionsAnchor { get { return options; } }
        public bool InputFocused { get { return input.Focused; } }
        public string PresetText { get { return chip.Text; } set { chip.Text = value; PerformLayout(); chip.Invalidate(); } }

        public void SetChipOpen(Control anchor, bool open)
        {
            PresetChip target = anchor as PresetChip;
            if (target == null) return;
            target.Open = open;
            target.Invalidate();
        }
        public void FocusInput() { input.Focus(); }

        public void Clear() { input.Clear(); }

        private void Submit()
        {
            if (SubmitRequested != null) SubmitRequested(input.Text);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (download == null || chip == null || options == null || input == null) return; // Layout can run while the constructor sets Height.
            int pad = Dpi.Px(5);
            int buttonHeight = Height - pad * 2;
            download.Size = new Size(download.Width, buttonHeight);
            download.Location = new Point(Width - pad - download.Width, pad);
            options.Height = buttonHeight;
            options.Width = options.PreferredWidth;
            options.Location = new Point(download.Left - Dpi.Px(6) - options.Width, pad);
            chip.Height = buttonHeight;
            chip.Width = chip.PreferredWidth;
            chip.Location = new Point(options.Left - Dpi.Px(6) - chip.Width, pad);
            int left = Dpi.Px(40);
            input.Location = new Point(left, (Height - input.PreferredHeight) / 2);
            input.Width = Math.Max(Dpi.Px(40), chip.Left - Dpi.Px(10) - left);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            input.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush clear = new SolidBrush(Parent != null ? Parent.BackColor : Theme.BgTopBar)) g.FillRectangle(clear, ClientRectangle);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, Dpi.Px(12)))
            using (SolidBrush fill = new SolidBrush(Theme.BgInput))
            using (Pen border = new Pen(focused ? Theme.BorderFocus : Theme.BorderHover, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            Icons.Draw(g, IconKind.Link, new Rectangle(Dpi.Px(12), 0, Dpi.Px(18), Height), Theme.TextMuted);
        }

        // "MP4 · Best · Downloads ⌄"
        private sealed class PresetChip : Control
        {
            private bool hovered;
            public bool Open;

            public PresetChip()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                Font = Theme.GetFont("Segoe UI", 9.5F);
                AccessibleName = "Download settings";
                AccessibleRole = AccessibleRole.ButtonDropDown;
                TabStop = true;
            }

            public int PreferredWidth { get { return TextRenderer.MeasureText(Text ?? "", Font).Width + Dpi.Px(40); } }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down) { OnClick(EventArgs.Empty); e.Handled = true; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush clear = new SolidBrush(Theme.BgInput)) g.FillRectangle(clear, ClientRectangle);
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, Dpi.Px(8)))
                using (SolidBrush fill = new SolidBrush(hovered || Open ? Theme.BgSurfaceHover : Theme.BgSurface))
                using (Pen border = new Pen(Focused || Open ? Theme.BorderHover : Theme.BorderSubtle))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                Rectangle text = new Rectangle(Dpi.Px(12), 0, Width - Dpi.Px(36), Height);
                TextRenderer.DrawText(g, Text, Font, text, Theme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                Theme.DrawChevron(g, new Point(Width - Dpi.Px(16), Height / 2 + Dpi.Px(1)), Theme.TextSecondary, Open);
            }
        }
    }
}
