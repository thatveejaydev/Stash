using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MediaDownloader
{
    internal sealed class StarIconButton : ModernButton
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float radius = Dpi.Px(8);
            PointF[] points = new PointF[10];
            for (int i = 0; i < points.Length; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / 5;
                float length = i % 2 == 0 ? radius : radius * 0.45f;
                points[i] = new PointF((float)Math.Cos(angle) * length, (float)Math.Sin(angle) * length);
            }
            using (GraphicsPath star = new GraphicsPath())
            {
                star.AddPolygon(points);
                RectangleF bounds = star.GetBounds();
                using (Matrix offset = new Matrix())
                {
                    offset.Translate((ClientSize.Width - Dpi.Px(1)) / 2f - (bounds.Left + bounds.Right) / 2f,
                        (ClientSize.Height - Dpi.Px(1)) / 2f - (bounds.Top + bounds.Bottom) / 2f);
                    star.Transform(offset);
                }
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush brush = new SolidBrush(Enabled ? ForeColor : Theme.TextMuted))
                    e.Graphics.FillPath(brush, star);
            }
        }
    }
}
