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
            Size = new Size(154, 40);
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

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = isPressed ? Theme.BrandPurplePress : (isHovered ? Theme.BrandPurpleHover : Theme.BrandPurple);

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, 6))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }

            int iconCenterX = 33;
            int iconCenterY = Height / 2;
            DrawClipboardPasteIcon(g, new Point(iconCenterX, iconCenterY), Color.White, fill);

            Rectangle textRect = new Rectangle(48, 0, Width - 54, Height);
            TextRenderer.DrawText(g, "Paste Link", Font, textRect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawClipboardPasteIcon(Graphics g, Point center, Color fg, Color bg)
        {
            // Crisp Clipboard + Down Arrow icon
            Rectangle boardRect = new Rectangle(center.X - 6, center.Y - 6, 12, 14);
            using (Pen p = new Pen(fg, 1.6f))
            using (GraphicsPath boardPath = Theme.CreateRoundedRectangle(boardRect, 2))
            {
                g.DrawPath(p, boardPath);
            }

            // Top clip tab
            Rectangle clipClear = new Rectangle(center.X - 4, center.Y - 8, 8, 4);
            using (SolidBrush clearBrush = new SolidBrush(bg))
            {
                g.FillRectangle(clearBrush, clipClear);
            }
            Rectangle clipRect = new Rectangle(center.X - 3, center.Y - 8, 6, 3);
            using (Pen p = new Pen(fg, 1.5f))
            using (GraphicsPath clipPath = Theme.CreateRoundedRectangle(clipRect, 1))
            {
                g.DrawPath(p, clipPath);
            }

            // Downward arrow inside the clipboard
            using (Pen arrowPen = new Pen(fg, 1.5f))
            {
                arrowPen.StartCap = LineCap.Round;
                arrowPen.EndCap = LineCap.Round;
                int ax = center.X;
                int topY = center.Y - 2;
                int botY = center.Y + 4;
                g.DrawLine(arrowPen, ax, topY, ax, botY);
                g.DrawLine(arrowPen, ax - 2, botY - 2, ax, botY);
                g.DrawLine(arrowPen, ax + 2, botY - 2, ax, botY);
            }
        }
    }
}
