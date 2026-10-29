using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Custom Owner-Drawn Popup Menu Item (Supports aligned Checkmark + Main Title + Right Pill Tag or Bottom Subtext + Optional Remove Button)
    public class PopupMenuRow : Control
    {
        public bool IsSelected { get; set; }
        public string CheckGlyph { get; set; }
        public string TitleText { get; set; }
        public string RightBadgeText { get; set; }
        public string SubText { get; set; }
        public Color TitleOverrideColor { get; set; }
        public bool AllowRemove { get; set; }

        private bool isHovered = false;
        private bool isRemoveHovered = false;

        public event EventHandler RemoveClicked;

        public PopupMenuRow(string title, string rightBadge, string subText, bool selected, string checkGlyph = "✓")
        {
            TitleText = title;
            RightBadgeText = rightBadge;
            SubText = subText;
            IsSelected = selected;
            CheckGlyph = checkGlyph;
            TitleOverrideColor = Color.Empty;
            AllowRemove = false;
            Height = string.IsNullOrEmpty(subText) ? Dpi.Px(34) : Dpi.Px(46);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private Rectangle GetRemoveButtonRect()
        {
            return new Rectangle(Width - Dpi.Px(32), (Height - Dpi.Px(24)) / 2, Dpi.Px(24), Dpi.Px(24));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (AllowRemove)
            {
                bool hitRemove = GetRemoveButtonRect().Contains(e.Location);
                if (hitRemove != isRemoveHovered)
                {
                    isRemoveHovered = hitRemove;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            isRemoveHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && AllowRemove && GetRemoveButtonRect().Contains(e.Location))
            {
                if (RemoveClicked != null) RemoveClicked(this, EventArgs.Empty);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = isHovered
                ? Color.FromArgb(44, 48, 60)
                : (IsSelected ? Color.FromArgb(36, 39, 50) : Theme.BgSurface);

            using (SolidBrush b = new SolidBrush(bg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            Color mainColor = TitleOverrideColor != Color.Empty
                ? TitleOverrideColor
                : (IsSelected ? Theme.BrandPurpleLight : Theme.TextPrimary);

            // Fixed 28px left gutter so all rows align to the exact same pixel column
            if (!string.IsNullOrEmpty(CheckGlyph) && (IsSelected || CheckGlyph == "☐" || CheckGlyph == "☑"))
            {
                using (Font fCheck = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
                {
                    Rectangle rCheck = new Rectangle(Dpi.Px(6), 0, Dpi.Px(22), string.IsNullOrEmpty(SubText) ? Height : Dpi.Px(24));
                    if (!string.IsNullOrEmpty(SubText)) rCheck.Y = Dpi.Px(4);
                    TextRenderer.DrawText(g, CheckGlyph, fCheck, rCheck,
                        IsSelected ? Theme.BrandPurpleLight : Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            int contentLeft = string.IsNullOrEmpty(CheckGlyph) ? Dpi.Px(14) : Dpi.Px(32);
            int rightMargin = AllowRemove ? Dpi.Px(38) : Dpi.Px(10);

            if (string.IsNullOrEmpty(SubText))
            {
                // Single-line mode with optional right-aligned detail badge
                int rightReserve = string.IsNullOrEmpty(RightBadgeText) ? rightMargin : Dpi.Px(120);
                Rectangle rTitle = new Rectangle(contentLeft, 0, Width - contentLeft - rightReserve, Height);
                using (Font fTitle = new Font("Segoe UI", 9.2F, IsSelected ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, TitleText, fTitle, rTitle, mainColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                if (!string.IsNullOrEmpty(RightBadgeText))
                {
                    Rectangle rRight = new Rectangle(Width - Dpi.Px(125), 0, Dpi.Px(115), Height);
                    using (Font fSub = new Font("Segoe UI", 8.2F, FontStyle.Regular))
                    {
                        TextRenderer.DrawText(g, RightBadgeText, fSub, rRight, Theme.TextMuted,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
            else
            {
                // Two-line mode: Title on top, smaller muted subtext directly underneath
                Rectangle rTitle = new Rectangle(contentLeft, Dpi.Px(4), Width - contentLeft - rightMargin, Dpi.Px(20));
                using (Font fTitle = new Font("Segoe UI", 9.2F, (IsSelected || AllowRemove) ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, TitleText, fTitle, rTitle, mainColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                Rectangle rSub = new Rectangle(contentLeft, Dpi.Px(23), Width - contentLeft - rightMargin, Dpi.Px(17));
                using (Font fTiny = new Font("Segoe UI", 7.3F, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, SubText, fTiny, rSub, Theme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            // Draw right-side Remove ('✕') button when hovering a removable folder row
            if (AllowRemove && (isHovered || isRemoveHovered))
            {
                Rectangle remRect = GetRemoveButtonRect();
                if (isRemoveHovered)
                {
                    using (GraphicsPath rp = Theme.CreateRoundedRectangle(remRect, 5))
                    using (SolidBrush rb = new SolidBrush(Color.FromArgb(62, 28, 35)))
                    {
                        g.FillPath(rb, rp);
                    }
                }

                using (Font fRem = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                {
                    Color xCol = isRemoveHovered ? Theme.AccentDanger : Theme.TextSecondary;
                    TextRenderer.DrawText(g, "✕", fRem, remRect, xCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
