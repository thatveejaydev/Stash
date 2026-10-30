using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Purple "Paste Link" Hero Button (Clipboard + Down-Arrow icon + "Paste Link")
    public class PasteLinkHeroButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public PasteLinkHeroButton()
        {
            Size = new Size(Dpi.Px(112), Dpi.Px(40));
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Bold);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) isPressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
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

            Rectangle rect = new Rectangle(0, 0, Width - Dpi.Px(1), Height - Dpi.Px(1));
            Color fill = isPressed ? Theme.BrandPurplePress : (isHovered ? Theme.BrandPurpleHover : Theme.BrandPurple);

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, 6))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }

            Rectangle textRect = new Rectangle(0, 0, Width, Height);
            TextRenderer.DrawText(g, "Paste Link", Font, textRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

    }
}
