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
        private bool iconOnly;
        public bool IconOnly
        {
            get { return iconOnly; }
            set { iconOnly = value; RecalculateWidth(); Invalidate(); }
        }
        private bool isHovered = false;
        private bool isOpen = false;

        public event EventHandler Clicked;

        public InlineFlowSelector(string prefix, string initialValue)
        {
            PrefixLabel = prefix;
            ValueLabel = initialValue;
            Height = 40;
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
            if (IconOnly) { Width = 40; return; }
            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                string label = PrefixLabel == "Quality" && ValueLabel == "Best" ? "Best quality" : ValueLabel;
                Width = TextRenderer.MeasureText(label, fValue).Width + 60;
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

            // Only divide adjacent settings; the last dropdown has no trailing rule.
            if (!string.IsNullOrEmpty(PrefixLabel))
            {
                using (Pen separator = new Pen(Theme.BorderSubtle))
                    g.DrawLine(separator, Width - 1, 12, Width - 1, Height - 12);
            }

            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                Color iconColor = Enabled ? Theme.TextSecondary : Theme.TextMuted;
                using (Pen icon = new Pen(iconColor, 1.3f))
                {
                    int cy = Height / 2;
                    GraphicsState iconState = g.Save();
                    g.TranslateTransform(20, cy);
                    g.ScaleTransform(0.8f, 0.8f);
                    g.TranslateTransform(-20, -cy);
                    if (PrefixLabel == "Download")
                    {
                        g.DrawRectangle(icon, 12, cy - 5, 11, 10);
                        g.DrawLines(icon, new Point[] { new Point(23, cy - 2), new Point(28, cy - 5), new Point(28, cy + 5), new Point(23, cy + 2) });
                    }
                    else if (PrefixLabel == "Save to")
                    {
                        g.DrawLines(icon, new Point[] { new Point(12, cy + 5), new Point(12, cy - 5), new Point(18, cy - 5), new Point(21, cy - 2), new Point(27, cy - 2), new Point(27, cy + 5), new Point(12, cy + 5) });
                    }
                    else if (PrefixLabel == "Quality")
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int y = cy - 5 + i * 5;
                            g.DrawLine(icon, 12, y, 27, y);
                            g.DrawRectangle(icon, i == 1 ? 21 : 15, y - 2, 3, 4);
                        }
                    }
                    else
                    {
                        PointF[] gear = new PointF[32];
                        for (int i = 0; i < gear.Length; i++)
                        {
                            double angle = i * Math.PI * 2 / gear.Length;
                            float radius = i % 4 == 0 || i % 4 == 3 ? 7.5f : 5.8f;
                            gear[i] = new PointF(20 + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
                        }
                        g.DrawPolygon(icon, gear);
                        g.DrawEllipse(icon, 17.5f, cy - 2.5f, 5, 5);
                    }
                    g.Restore(iconState);
                }
                if (IconOnly) return;
                int x = 35;
                string label = PrefixLabel == "Quality" && ValueLabel == "Best" ? "Best quality" : ValueLabel;
                Size sVal = TextRenderer.MeasureText(g, label, fValue, new Size(500, Height), TextFormatFlags.NoPadding);
                Rectangle rVal = new Rectangle(x, 0, sVal.Width, Height);
                TextRenderer.DrawText(g, label, fValue, rVal,
                    Enabled ? Theme.TextPrimary : Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += sVal.Width + 11;

                Theme.DrawChevronDown(g, new Point(x, Height / 2 + 1),
                    Enabled ? Theme.TextSecondary : Theme.TextMuted);
            }
        }
    }
}
