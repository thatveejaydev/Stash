using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Custom Rounded Card Panel
    public class CardPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color CardFillColor { get; set; }
        public Color CardBorderColor { get; set; }

        public CardPanel()
        {
            CornerRadius = 8;
            CardFillColor = Theme.BgSurface;
            CardBorderColor = Theme.BorderSubtle;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgWindow;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush b = new SolidBrush(CardFillColor))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(CardBorderColor, 1f))
                {
                    g.DrawPath(p, path);
                }
            }
        }
    }
}
