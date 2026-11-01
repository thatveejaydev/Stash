using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MediaDownloader
{
    internal sealed class InformationIconButton : ModernButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = (ClientSize.Width - Dpi.Px(1)) / 2f;
            float y = (ClientSize.Height - Dpi.Px(1)) / 2f;
            float radius = Dpi.Px(6);
            Color color = Enabled ? ForeColor : Theme.TextMuted;
            using (Pen outline = new Pen(color, Dpi.Px(1)))
            using (SolidBrush dot = new SolidBrush(color))
            {
                g.DrawEllipse(outline, x - radius, y - radius, radius * 2, radius * 2);
                float dotSize = Dpi.Px(1.5f);
                g.FillEllipse(dot, x - dotSize / 2, y - Dpi.Px(3.5f), dotSize, dotSize);
                g.DrawLine(outline, x, y - Dpi.Px(0.5f), x, y + Dpi.Px(3.5f));
            }
        }
    }
}
