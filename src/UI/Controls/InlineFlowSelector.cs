using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Inline Frameless Flow Selector ("Download  Video ⌄", "Quality  Best ⌄", etc.)
    public class InlineFlowSelector : Control
    {
        public string PrefixLabel { get; set; }
        public string ValueLabel { get; set; }
        private bool isHovered = false;
        private bool isOpen = false;

        public event EventHandler Clicked;

        public InlineFlowSelector(string prefix, string initialValue)
        {
            PrefixLabel = prefix;
            ValueLabel = initialValue;
            Height = 36;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            RecalculateWidth();
        }

        public void SetValueLabel(string val)
        {
            ValueLabel = val;
            RecalculateWidth();
            Invalidate();
        }

        public void SetOpenState(bool open)
        {
            isOpen = open;
            Invalidate();
        }

        public void RecalculateWidth()
        {
            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                int w1 = string.IsNullOrEmpty(PrefixLabel) ? 0 : TextRenderer.MeasureText(PrefixLabel, fPrefix).Width;
                int w2 = TextRenderer.MeasureText(ValueLabel, fValue).Width;
                Width = w1 + w2 + 30;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Enabled && e.Button == MouseButtons.Left)
            {
                if (Clicked != null) Clicked(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgTopBar;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            if ((isHovered || isOpen) && Enabled)
            {
                Rectangle pill = new Rectangle(0, 2, Width - 1, Height - 4);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(pill, 6))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(38, 42, 52)))
                {
                    g.FillPath(hb, path);
                }
            }

            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                int x = 6;
                if (!string.IsNullOrEmpty(PrefixLabel))
                {
                    Size sPref = TextRenderer.MeasureText(g, PrefixLabel, fPrefix, new Size(500, Height), TextFormatFlags.NoPadding);
                    Rectangle rPref = new Rectangle(x, 0, sPref.Width, Height);
                    TextRenderer.DrawText(g, PrefixLabel, fPrefix, rPref,
                        Enabled ? Theme.TextSecondary : Theme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += sPref.Width + 6;
                }

                Size sVal = TextRenderer.MeasureText(g, ValueLabel, fValue, new Size(500, Height), TextFormatFlags.NoPadding);
                Rectangle rVal = new Rectangle(x, 0, sVal.Width, Height);
                TextRenderer.DrawText(g, ValueLabel, fValue, rVal,
                    Enabled ? Theme.TextPrimary : Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += sVal.Width + 11;

                Theme.DrawChevronDown(g, new Point(x, Height / 2 + 1),
                    Enabled ? Theme.TextSecondary : Theme.TextMuted);
            }
        }
    }
}
