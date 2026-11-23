using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Stash
{
    internal enum IconKind
    {
        Link, Folder, Play, Pause, Close, Retry, Info, More, Gear, Cookie, Download, Video, Audio, Star, Warning, Transcript
    }

    // Line icons drawn on a 16-unit grid and scaled to the screen DPI, so every
    // surface (cards, footer, dialogs) shares one consistent icon style.
    internal static class Icons
    {
        internal static void Draw(Graphics g, IconKind kind, Rectangle bounds, Color color, float size = 16f)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Dpi.Factor * size / 16f;
            g.TranslateTransform(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
            g.ScaleTransform(scale, scale);
            g.TranslateTransform(-8f, -8f);
            using (Pen pen = new Pen(color, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush brush = new SolidBrush(color))
            {
                switch (kind)
                {
                    case IconKind.Link:
                        GraphicsState link = g.Save();
                        g.TranslateTransform(8f, 8f);
                        g.RotateTransform(-45f);
                        using (GraphicsPath a = Rounded(new RectangleF(-7f, -2.6f, 7.6f, 5.2f), 2.6f)) g.DrawPath(pen, a);
                        using (GraphicsPath b = Rounded(new RectangleF(-0.6f, -2.6f, 7.6f, 5.2f), 2.6f)) g.DrawPath(pen, b);
                        g.Restore(link);
                        break;
                    case IconKind.Folder:
                        g.DrawLines(pen, new[] { new PointF(1.5f, 13f), new PointF(1.5f, 3f), new PointF(6f, 3f), new PointF(7.5f, 5f),
                            new PointF(14.5f, 5f), new PointF(14.5f, 13f), new PointF(1.5f, 13f) });
                        break;
                    case IconKind.Play:
                        g.DrawPolygon(pen, new[] { new PointF(4.5f, 2.5f), new PointF(13f, 8f), new PointF(4.5f, 13.5f) });
                        break;
                    case IconKind.Pause:
                        g.DrawLine(pen, 5.5f, 3f, 5.5f, 13f);
                        g.DrawLine(pen, 10.5f, 3f, 10.5f, 13f);
                        break;
                    case IconKind.Close:
                        g.DrawLine(pen, 3.5f, 3.5f, 12.5f, 12.5f);
                        g.DrawLine(pen, 12.5f, 3.5f, 3.5f, 12.5f);
                        break;
                    case IconKind.Retry:
                        g.DrawArc(pen, 2.5f, 2.5f, 11f, 11f, -60f, 300f);
                        g.DrawLines(pen, new[] { new PointF(10.5f, 1.5f), new PointF(11.2f, 3.8f), new PointF(8.8f, 4.6f) });
                        break;
                    case IconKind.Info:
                        g.DrawEllipse(pen, 1.5f, 1.5f, 13f, 13f);
                        g.FillEllipse(brush, 7.15f, 4.2f, 1.7f, 1.7f);
                        g.DrawLine(pen, 8f, 7.5f, 8f, 11.5f);
                        break;
                    case IconKind.Warning:
                        g.DrawPolygon(pen, new[] { new PointF(8f, 1.8f), new PointF(14.8f, 14f), new PointF(1.2f, 14f) });
                        g.DrawLine(pen, 8f, 6.2f, 8f, 9.6f);
                        g.FillEllipse(brush, 7.2f, 11.2f, 1.6f, 1.6f);
                        break;
                    case IconKind.More:
                        for (int i = 0; i < 3; i++) g.FillEllipse(brush, 2.2f + i * 5f, 7f, 2f, 2f);
                        break;
                    case IconKind.Gear:
                        PointF[] gear = new PointF[32];
                        for (int i = 0; i < gear.Length; i++)
                        {
                            double angle = i * Math.PI * 2 / gear.Length;
                            float radius = i % 4 == 0 || i % 4 == 3 ? 7f : 5.4f;
                            gear[i] = new PointF(8f + radius * (float)Math.Cos(angle), 8f + radius * (float)Math.Sin(angle));
                        }
                        g.DrawPolygon(pen, gear);
                        g.DrawEllipse(pen, 5.7f, 5.7f, 4.6f, 4.6f);
                        break;
                    case IconKind.Cookie:
                        using (GraphicsPath cookie = new GraphicsPath())
                        {
                            cookie.AddArc(1f, 1f, 14f, 14f, -20f, 300f);
                            cookie.AddBezier(cookie.GetLastPoint(), new PointF(8.5f, 4.5f), new PointF(11.5f, 7f), new PointF(14.6f, 5.6f));
                            cookie.CloseFigure();
                            g.DrawPath(pen, cookie);
                        }
                        g.FillEllipse(brush, 4.3f, 6.3f, 2.2f, 2.2f);
                        g.FillEllipse(brush, 9.3f, 9.5f, 2.2f, 2.2f);
                        g.FillEllipse(brush, 5.5f, 10.8f, 2.2f, 2.2f);
                        break;
                    case IconKind.Download:
                        g.DrawLine(pen, 8f, 1.5f, 8f, 10f);
                        g.DrawLines(pen, new[] { new PointF(4.5f, 6.8f), new PointF(8f, 10.3f), new PointF(11.5f, 6.8f) });
                        g.DrawLines(pen, new[] { new PointF(2f, 11f), new PointF(2f, 14.5f), new PointF(14f, 14.5f), new PointF(14f, 11f) });
                        break;
                    case IconKind.Video:
                        using (GraphicsPath body = Rounded(new RectangleF(1.5f, 4f, 9f, 8f), 1.5f)) g.DrawPath(pen, body);
                        g.DrawLines(pen, new[] { new PointF(10.5f, 7f), new PointF(14.5f, 4.8f), new PointF(14.5f, 11.2f), new PointF(10.5f, 9f) });
                        break;
                    case IconKind.Audio:
                        g.DrawLine(pen, 10f, 2.5f, 10f, 11.5f);
                        g.DrawLine(pen, 10f, 2.5f, 13.5f, 3.8f);
                        g.DrawEllipse(pen, 4.5f, 10f, 5.5f, 4f);
                        break;
                    case IconKind.Transcript:
                        g.DrawPolygon(pen, new[] { new PointF(3f, 1.5f), new PointF(9f, 1.5f), new PointF(13f, 5.5f),
                            new PointF(13f, 14.5f), new PointF(3f, 14.5f) });
                        g.DrawLines(pen, new[] { new PointF(9f, 1.5f), new PointF(9f, 5.5f), new PointF(13f, 5.5f) });
                        g.DrawLine(pen, 5.5f, 8.25f, 10.5f, 8.25f);
                        g.DrawLine(pen, 5.5f, 11f, 9f, 11f);
                        break;
                    case IconKind.Star:
                        PointF[] star = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double angle = -Math.PI / 2 + i * Math.PI / 5;
                            float radius = i % 2 == 0 ? 7f : 3f;
                            star[i] = new PointF(8f + radius * (float)Math.Cos(angle), 8.4f + radius * (float)Math.Sin(angle));
                        }
                        g.DrawPolygon(pen, star);
                        break;
                }
            }
            g.Restore(state);
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // A small borderless icon button for toolbars, footers and dialogs.
    internal sealed class IconButton : System.Windows.Forms.Control
    {
        private bool hovered, pressed;
        public IconKind Icon { get; set; }
        public Color IconColor { get; set; }
        public Color HoverIconColor { get; set; }

        public IconButton(IconKind icon, string accessibleName)
        {
            Icon = icon;
            IconColor = Theme.TextMuted;
            HoverIconColor = Theme.TextPrimary;
            AccessibleName = accessibleName;
            AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton;
            Size = new Size(Dpi.Px(30), Dpi.Px(30));
            Cursor = System.Windows.Forms.Cursors.Hand;
            TabStop = true;
            SetStyle(System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint |
                System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer | System.Windows.Forms.ControlStyles.SupportsTransparentBackColor, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(System.Windows.Forms.MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(System.Windows.Forms.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == System.Windows.Forms.Keys.Enter || e.KeyCode == System.Windows.Forms.Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush clear = new SolidBrush(Parent != null ? Parent.BackColor : Theme.BgWindow)) g.FillRectangle(clear, ClientRectangle);
            if (hovered || Focused)
            {
                using (GraphicsPath path = Theme.CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Dpi.Px(6)))
                using (SolidBrush fill = new SolidBrush(pressed ? Theme.BgSurface : Theme.BgSurfaceHover)) g.FillPath(fill, path);
            }
            Icons.Draw(g, Icon, ClientRectangle, Enabled ? (hovered ? HoverIconColor : IconColor) : Theme.BorderHover);
        }
    }
}
