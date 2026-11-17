using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Stash
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
        public static readonly Color TextSecondary = Color.FromArgb(185, 191, 204);
        public static readonly Color TextMuted = Color.FromArgb(150, 158, 174);

        // Vibrant Purple Brand Colors
        public static readonly Color BrandPurple = Color.FromArgb(124, 58, 237);
        public static readonly Color BrandPurpleHover = Color.FromArgb(139, 92, 246);
        public static readonly Color BrandPurplePress = Color.FromArgb(109, 40, 217);
        public static readonly Color BrandPurpleLight = Color.FromArgb(196, 181, 253);

        public static readonly Color AccentSuccess = Color.FromArgb(34, 197, 94);
        public static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
        public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);
        public static readonly Color AccentPurple = Color.FromArgb(168, 85, 247);

        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // Shared, never-disposed fonts. Callers must not dispose the returned instance.
        public static Font GetFont(string family, float size, FontStyle style = FontStyle.Regular)
        {
            string key = family + "|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            lock (fonts)
            {
                Font font;
                if (!fonts.TryGetValue(key, out font))
                {
                    font = new Font(family, size, style);
                    fonts.Add(key, font);
                }
                return font;
            }
        }

        public static readonly Color AccentText = Color.FromArgb(180, 160, 252);
        public static readonly Color DangerText = Color.FromArgb(252, 165, 165);

        // The one button style used across the window and every dialog: primary buttons are the
        // single accent action in a view; secondary buttons are outlined and quiet.
        public static ModernButton CreateButton(string text, bool primary, int minimumWidth = 88)
        {
            Font font = GetFont("Segoe UI Semibold", 9F);
            int width = Math.Max(Dpi.Px(minimumWidth), TextRenderer.MeasureText(text, font).Width + Dpi.Px(32));
            return new ModernButton
            {
                Text = text,
                Size = new Size(width, Dpi.Px(34)),
                Font = font,
                CornerRadius = Dpi.Px(8),
                BaseColor = primary ? BrandPurple : BgSurface,
                HoverColor = primary ? BrandPurpleHover : BgSurfaceHover,
                BorderColor = primary ? Color.Empty : BorderSubtle,
                ForeColor = primary ? Color.White : TextPrimary,
                Margin = new Padding(Dpi.Px(8), 0, 0, 0)
            };
        }

        // Standard dialog footer: buttons right-aligned in the order given (left to right).
        public static Panel CreateDialogFooter(params Control[] buttons)
        {
            FlowLayoutPanel footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = Dpi.Px(62),
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(Dpi.Px(16), Dpi.Px(14), Dpi.Px(16), 0),
                BackColor = BgTopBar
            };
            footer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(BorderSubtle)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            for (int i = buttons.Length - 1; i >= 0; i--) footer.Controls.Add(buttons[i]);
            return footer;
        }

        // Standard dialog heading: title with an optional muted subtitle.
        public static Label CreateDialogTitle(string text)
        {
            return new Label
            {
                Text = text, AutoSize = true, UseMnemonic = false, BackColor = Color.Transparent,
                Font = GetFont("Segoe UI Semibold", 12.5F), ForeColor = TextPrimary
            };
        }

        public static Label CreateDialogText(string text, int maxWidth)
        {
            return new Label
            {
                Text = text, AutoSize = true, MaximumSize = new Size(maxWidth, 0), UseMnemonic = false, BackColor = Color.Transparent,
                Font = GetFont("Segoe UI", 9.5F), ForeColor = TextSecondary
            };
        }

        // Draws the standard bordered surface used by popup panels.
        public static Panel CreatePopupPanel(int width, int height)
        {
            Panel panel = new Panel { BackColor = BgSurface, Width = width, Height = height };
            panel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(BorderHover, 1f))
                    e.Graphics.DrawRectangle(p, 0, 0, panel.Width - Dpi.Px(1), panel.Height - Dpi.Px(1));
            };
            return panel;
        }

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

        public static void DrawChevron(Graphics g, Point center, Color color, bool pointsUp = false)
        {
            using (Pen p = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                int direction = pointsUp ? -1 : 1;
                Point[] pts = new Point[]
                {
                    new Point(center.X - Dpi.Px(5), center.Y - direction * Dpi.Px(2)),
                    new Point(center.X, center.Y + direction * Dpi.Px(3)),
                    new Point(center.X + Dpi.Px(5), center.Y - direction * Dpi.Px(2))
                };
                g.DrawLines(p, pts);
            }
        }
    }
}
