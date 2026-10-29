using System.Drawing;
using System.Drawing.Drawing2D;

namespace MediaDownloader
{
    // Modern Palette Constants (Purple Primary Accent)
    internal static class Theme
    {
        public static readonly Color BgWindow = Color.FromArgb(20, 21, 26);
        public static readonly Color BgTopBar = Color.FromArgb(26, 28, 34);
        public static readonly Color BgSurface = Color.FromArgb(30, 33, 40);
        public static readonly Color BgSurfaceHover = Color.FromArgb(38, 42, 52);
        public static readonly Color BgInput = Color.FromArgb(17, 19, 24);
        public static readonly Color BorderSubtle = Color.FromArgb(46, 50, 62);
        public static readonly Color BorderHover = Color.FromArgb(75, 82, 100);
        public static readonly Color BorderFocus = Color.FromArgb(139, 92, 246);

        public static readonly Color TextPrimary = Color.FromArgb(244, 245, 248);
        public static readonly Color TextSecondary = Color.FromArgb(158, 165, 180);
        public static readonly Color TextMuted = Color.FromArgb(110, 117, 132);

        // Vibrant Purple Brand Colors
        public static readonly Color BrandPurple = Color.FromArgb(124, 58, 237);
        public static readonly Color BrandPurpleHover = Color.FromArgb(139, 92, 246);
        public static readonly Color BrandPurplePress = Color.FromArgb(109, 40, 217);
        public static readonly Color BrandPurpleLight = Color.FromArgb(196, 181, 253);

        public static readonly Color AccentSuccess = Color.FromArgb(34, 197, 94);
        public static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
        public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);
        public static readonly Color AccentPurple = Color.FromArgb(168, 85, 247);

        public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (radius <= 0 || rect.Width <= d || rect.Height <= d)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawChevronDown(Graphics g, Point center, Color color)
        {
            using (Pen p = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                Point[] pts = new Point[]
                {
                    new Point(center.X - 5, center.Y - 2),
                    new Point(center.X, center.Y + 3),
                    new Point(center.X + 5, center.Y - 2)
                };
                g.DrawLines(p, pts);
            }
        }
    }
}
