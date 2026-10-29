using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Custom Rounded Modern Button
    public class ModernButton : Button
    {
        public Color BaseColor { get; set; }
        public Color HoverColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }
        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            BaseColor = Theme.BgSurface;
            HoverColor = Theme.BgSurfaceHover;
            BorderColor = Color.Empty;
            CornerRadius = 6;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
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
            isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left) isPressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgWindow;
            using (SolidBrush clearBrush = new SolidBrush(parentColor))
            {
                g.FillRectangle(clearBrush, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled
                ? Color.FromArgb(30, 34, 45)
                : (isPressed ? ControlPaint.Dark(BaseColor, 0.05f) : (isHovered ? HoverColor : BaseColor));

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush b = new SolidBrush(fill))
                {
                    g.FillPath(b, path);
                }

                if (BorderColor != Color.Empty)
                {
                    using (Pen p = new Pen(isHovered ? Theme.BorderHover : BorderColor, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
            }

            Color textCol = Enabled ? ForeColor : Theme.TextMuted;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
