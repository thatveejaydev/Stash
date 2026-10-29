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
            Height = Dpi.Px(40);
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
            if (IconOnly) { Width = Dpi.Px(40); return; }
            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                string label = PrefixLabel == "Quality" && ValueLabel == "Best" ? "Best quality" : ValueLabel;
                Width = TextRenderer.MeasureText(label, fValue).Width + Dpi.Px(60);
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
                Rectangle pill = new Rectangle(0, Dpi.Px(2), Width - Dpi.Px(1), Height - Dpi.Px(4));
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
                    g.DrawLine(separator, Width - Dpi.Px(1), Dpi.Px(12), Width - Dpi.Px(1), Height - Dpi.Px(12));
            }

            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                Color iconColor = Enabled ? Theme.TextSecondary : Theme.TextMuted;
                using (Pen icon = new Pen(iconColor, 1.3f))
                {
                    int cy = Height / 2;
                    GraphicsState iconState = g.Save();
                    g.TranslateTransform(Dpi.Px(20), cy);
                    g.ScaleTransform(0.8f, 0.8f);
                    g.TranslateTransform(-Dpi.Px(20), -cy);
                    if (PrefixLabel == "Download")
                    {
                        g.DrawRectangle(icon, Dpi.Px(12), cy - Dpi.Px(5), Dpi.Px(11), Dpi.Px(10));
                        g.DrawLines(icon, new Point[] { new Point(Dpi.Px(23), cy - Dpi.Px(2)), new Point(Dpi.Px(28), cy - Dpi.Px(5)), new Point(Dpi.Px(28), cy + Dpi.Px(5)), new Point(Dpi.Px(23), cy + Dpi.Px(2)) });
                    }
                    else if (PrefixLabel == "Save to")
                    {
                        g.DrawLines(icon, new Point[] { new Point(Dpi.Px(12), cy + Dpi.Px(5)), new Point(Dpi.Px(12), cy - Dpi.Px(5)), new Point(Dpi.Px(18), cy - Dpi.Px(5)), new Point(Dpi.Px(21), cy - Dpi.Px(2)), new Point(Dpi.Px(27), cy - Dpi.Px(2)), new Point(Dpi.Px(27), cy + Dpi.Px(5)), new Point(Dpi.Px(12), cy + Dpi.Px(5)) });
                    }
                    else if (PrefixLabel == "Quality")
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int y = cy - Dpi.Px(5) + i * Dpi.Px(5);
                            g.DrawLine(icon, Dpi.Px(12), y, Dpi.Px(27), y);
                            g.DrawRectangle(icon, i == 1 ? Dpi.Px(21) : Dpi.Px(15), y - Dpi.Px(2), Dpi.Px(3), Dpi.Px(4));
                        }
                    }
                    else
                    {
                        PointF[] gear = new PointF[32];
                        for (int i = 0; i < gear.Length; i++)
                        {
                            double angle = i * Math.PI * 2 / gear.Length;
                            float radius = i % 4 == 0 || i % 4 == 3 ? Dpi.Px(7.5f) : Dpi.Px(5.8f);
                            gear[i] = new PointF(Dpi.Px(20) + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
                        }
                        g.DrawPolygon(icon, gear);
                        g.DrawEllipse(icon, Dpi.Px(17.5f), cy - Dpi.Px(2.5f), Dpi.Px(5), Dpi.Px(5));
                    }
                    g.Restore(iconState);
                }
                if (IconOnly) return;
                int x = Dpi.Px(35);
                string label = PrefixLabel == "Quality" && ValueLabel == "Best" ? "Best quality" : ValueLabel;
                Size sVal = TextRenderer.MeasureText(g, label, fValue, new Size(Dpi.Px(500), Height), TextFormatFlags.NoPadding);
                Rectangle rVal = new Rectangle(x, 0, sVal.Width, Height);
                TextRenderer.DrawText(g, label, fValue, rVal,
                    Enabled ? Theme.TextPrimary : Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += sVal.Width + Dpi.Px(11);

                Theme.DrawChevronDown(g, new Point(x, Height / 2 + Dpi.Px(1)),
                    Enabled ? Theme.TextSecondary : Theme.TextMuted);
            }
        }
    }
}
