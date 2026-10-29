using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Virtual Smooth Queue Canvas for modern card-like rows
    public partial class QueueListControl : Control
    {
        private readonly List<DownloadQueueItem> items = new List<DownloadQueueItem>();
        public DownloadQueueItem SelectedItem { get; private set; }
        private DownloadQueueItem hoveredItem = null;
        private DarkScrollBar scrollBar;
        private static readonly int RowHeight = Dpi.Px(112);
        private static readonly int RowGap = Dpi.Px(10);

        public event EventHandler SelectionChanged;
        public event Action<DownloadQueueItem> PauseResumeRequested;
        public event Action<DownloadQueueItem> CancelRequested;
        public event Action<DownloadQueueItem> OpenFileFolderRequested;

        public QueueListControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.BgWindow;

            scrollBar = new DarkScrollBar
            {
                Dock = DockStyle.Right,
                Visible = false,
                SmallChange = RowHeight / 2,
                LargeChange = RowHeight * 2
            };
            scrollBar.Scroll += (s, e) => Invalidate();
            Controls.Add(scrollBar);
        }

        public void InsertItemAtTop(DownloadQueueItem item)
        {
            items.Insert(0, item);
            SelectedItem = item;
            UpdateScrollBar();
            if (scrollBar.Visible) scrollBar.Value = 0;
            Invalidate();
        }

        public void RemoveItem(DownloadQueueItem item)
        {
            items.Remove(item);
            if (SelectedItem == item)
            {
                SelectedItem = items.Count > 0 ? items[0] : null;
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            UpdateScrollBar();
            Invalidate();
        }

        public List<DownloadQueueItem> GetItems()
        {
            return new List<DownloadQueueItem>(items);
        }

        private void UpdateScrollBar()
        {
            int totalHeight = Dpi.Px(8);
            foreach (DownloadQueueItem item in items) totalHeight += GetRowHeight(item) + RowGap;
            if (totalHeight > ClientSize.Height && ClientSize.Height > 0)
            {
                scrollBar.Maximum = totalHeight;
                scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
                scrollBar.Visible = true;
            }
            else
            {
                scrollBar.Value = 0;
                scrollBar.Visible = false;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollBar();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (scrollBar.Visible)
            {
                int newVal = scrollBar.Value - (e.Delta / 2);
                int maxScroll = Math.Max(0, scrollBar.Maximum - scrollBar.LargeChange + 1);
                scrollBar.Value = Math.Max(0, Math.Min(maxScroll, newVal));
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DownloadQueueItem hit = HitTestItem(e.Location);
            if (hit != hoveredItem)
            {
                hoveredItem = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredItem != null)
            {
                hoveredItem = null;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index;
            Rectangle rowRect;
            DownloadQueueItem hit = HitTestItemDetailed(e.Location, out index, out rowRect);
            if (hit != null)
            {
                SelectedItem = hit;
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);

                Rectangle pauseRect = GetPauseResumeButtonRect(rowRect);
                if (CanPauseOrResume(hit.Status) && pauseRect.Contains(e.Location))
                {
                    if (PauseResumeRequested != null) PauseResumeRequested(hit);
                    Invalidate();
                    return;
                }

                Rectangle actionRect = GetActionButtonRect(rowRect);
                if (GetFolderButtonRect(rowRect).Contains(e.Location))
                {
                    if (OpenFileFolderRequested != null) OpenFileFolderRequested(hit);
                    return;
                }
                if (actionRect.Contains(e.Location))
                {
                    if (CanCancelStatus(hit.Status))
                    {
                        if (CancelRequested != null) CancelRequested(hit);
                    }
                }
                Invalidate();
            }
        }

        private bool IsActiveStatus(QueueItemStatus st)
        {
            return st == QueueItemStatus.Queued || st == QueueItemStatus.FetchingInfo ||
                   st == QueueItemStatus.Downloading || st == QueueItemStatus.Merging;
        }

        private bool CanPauseOrResume(QueueItemStatus st)
        {
            return st == QueueItemStatus.Queued || st == QueueItemStatus.FetchingInfo ||
                   st == QueueItemStatus.Downloading || st == QueueItemStatus.Paused;
        }

        private bool CanCancelStatus(QueueItemStatus st)
        {
            return IsActiveStatus(st) || st == QueueItemStatus.Paused;
        }

        private DownloadQueueItem HitTestItem(Point pt)
        {
            int idx;
            Rectangle r;
            return HitTestItemDetailed(pt, out idx, out r);
        }

        private int GetRowHeight(DownloadQueueItem item)
        {
            return item.Status == QueueItemStatus.Completed ? Dpi.Px(64) : RowHeight;
        }

        private DownloadQueueItem HitTestItemDetailed(Point pt, out int hitIndex, out Rectangle hitRect)
        {
            hitIndex = -1;
            hitRect = Rectangle.Empty;
            int yOffset = -scrollBar.Value + Dpi.Px(4);
            int rightPad = scrollBar.Visible ? scrollBar.Width + Dpi.Px(6) : Dpi.Px(2);
            int width = ClientSize.Width - rightPad - Dpi.Px(2);

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = new Rectangle(Dpi.Px(2), yOffset, width, GetRowHeight(items[i]));
                yOffset += r.Height + RowGap;
                if (r.Contains(pt))
                {
                    hitIndex = i;
                    hitRect = r;
                    return items[i];
                }
            }
            return null;
        }

        private Rectangle GetPauseResumeButtonRect(Rectangle rowRect)
        {
            return new Rectangle(rowRect.Right - Dpi.Px(102), rowRect.Bottom - Dpi.Px(38), Dpi.Px(26), Dpi.Px(26));
        }

        private Rectangle GetActionButtonRect(Rectangle rowRect)
        {
            return new Rectangle(rowRect.Right - Dpi.Px(38), rowRect.Height == Dpi.Px(64) ? rowRect.Y + Dpi.Px(19) : rowRect.Bottom - Dpi.Px(38), Dpi.Px(26), Dpi.Px(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            UpdateScrollBar();
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (SolidBrush bg = new SolidBrush(Theme.BgWindow))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            if (items.Count == 0)
            {
                DrawEmptyState(g);
                return;
            }

            int yOffset = -scrollBar.Value + Dpi.Px(4);
            int rightPad = scrollBar.Visible ? scrollBar.Width + Dpi.Px(6) : Dpi.Px(2);
            int width = ClientSize.Width - rightPad - Dpi.Px(2);

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle rowRect = new Rectangle(Dpi.Px(2), yOffset, width, GetRowHeight(items[i]));
                yOffset += rowRect.Height + RowGap;
                if (rowRect.Bottom < 0 || rowRect.Top > ClientSize.Height) continue;
                DrawQueueCard(g, items[i], rowRect);
            }
        }

        private void DrawEmptyState(Graphics g)
        {
            string title = "Ready to Download";
            string sub = "Copy any video or audio link and click 'Paste Link' (or press Ctrl+V anywhere in this window).";
            string credit = "Developed by Vee Jay";
            using (Font fTitle = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 9.5F, FontStyle.Regular))
            using (Font fCredit = new Font("Segoe UI", 8.5F, FontStyle.Regular))
            {
                Rectangle r1 = new Rectangle(0, ClientSize.Height / 2 - Dpi.Px(36), ClientSize.Width, Dpi.Px(26));
                Rectangle r2 = new Rectangle(0, ClientSize.Height / 2 - Dpi.Px(8), ClientSize.Width, Dpi.Px(24));
                Rectangle r3 = new Rectangle(0, ClientSize.Height / 2 + Dpi.Px(18), ClientSize.Width, Dpi.Px(22));
                TextRenderer.DrawText(g, title, fTitle, r1, Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, sub, fSub, r2, Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, credit, fCredit, r3, Color.FromArgb(92, 98, 114), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawLegacyQueueCard(Graphics g, DownloadQueueItem item, Rectangle rect)
        {
            bool isSelected = (item == SelectedItem);
            bool isHovered = (item == hoveredItem);

            Color cardFill = isSelected
                ? Color.FromArgb(35, 34, 48)
                : (isHovered ? Theme.BgSurfaceHover : Theme.BgSurface);

            Color borderCol = isSelected ? Theme.BrandPurple : Theme.BorderSubtle;

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, 7))
            {
                using (SolidBrush b = new SolidBrush(cardFill))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(borderCol, 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            Color statusColor = Theme.BrandPurple;
            if (item.Status == QueueItemStatus.Completed) statusColor = Theme.AccentSuccess;
            else if (item.Status == QueueItemStatus.Paused) statusColor = Theme.AccentWarning;
            else if (item.Status == QueueItemStatus.Error || item.Status == QueueItemStatus.Cancelled) statusColor = Theme.AccentDanger;
            else if (item.Status == QueueItemStatus.Merging) statusColor = Theme.AccentPurple;
            else if (item.Status == QueueItemStatus.Queued) statusColor = Theme.TextMuted;

            // Left Accent Pill Indicator
            Rectangle indicatorRect = new Rectangle(rect.X + Dpi.Px(10), rect.Y + Dpi.Px(12), Dpi.Px(4), rect.Height - Dpi.Px(24));
            using (GraphicsPath indPath = Theme.CreateRoundedRectangle(indicatorRect, 2))
            using (SolidBrush indBrush = new SolidBrush(statusColor))
            {
                g.FillPath(indBrush, indPath);
            }

            bool hasPauseBtn = CanPauseOrResume(item.Status);
            int rightControlsReserve = hasPauseBtn ? 82 : 50;

            // Format & Resolution Badge
            string badgeText = item.BadgeLabel;
            using (Font fBadge = new Font("Segoe UI Semibold", 7.8F, FontStyle.Bold))
            {
                Size badgeSize = TextRenderer.MeasureText(badgeText, fBadge);
                Rectangle badgeRect = new Rectangle(rect.X + Dpi.Px(24), rect.Y + Dpi.Px(11), badgeSize.Width + Dpi.Px(10), Dpi.Px(19));
                using (GraphicsPath bp = Theme.CreateRoundedRectangle(badgeRect, 4))
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(42, 38, 62)))
                {
                    g.FillPath(bb, bp);
                }
                TextRenderer.DrawText(g, badgeText, fBadge, badgeRect, Theme.BrandPurpleLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Title
                int titleLeft = badgeRect.Right + 10;
                int titleWidth = Math.Max(80, rect.Width - titleLeft - (rightControlsReserve + 210));
                Rectangle titleRect = new Rectangle(titleLeft, rect.Y + Dpi.Px(9), titleWidth, Dpi.Px(22));
                using (Font fTitle = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, item.Title, fTitle, titleRect, Theme.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            // Right telemetry & status label on top line
            string telemetryText = item.StatusText;
            if (item.Status == QueueItemStatus.Downloading)
            {
                telemetryText = string.Format("{0:0.0}%  •  {1}  •  ETA {2}", item.ProgressPercent, item.Speed, item.Eta);
            }
            else if (item.Status == QueueItemStatus.Paused)
            {
                telemetryText = string.Format("Paused  •  {0:0.0}%", item.ProgressPercent);
            }
            else if (item.Status == QueueItemStatus.Completed)
            {
                telemetryText = "Completed  •  100%";
            }

            Rectangle statusRect = new Rectangle(rect.Right - rightControlsReserve - Dpi.Px(208), rect.Y + Dpi.Px(10), Dpi.Px(204), Dpi.Px(20));
            using (Font fStatus = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, telemetryText, fStatus, statusRect, statusColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // Bottom Sleek Progress Bar Track
            Rectangle trackRect = new Rectangle(rect.X + Dpi.Px(24), rect.Bottom - Dpi.Px(16), rect.Width - Dpi.Px(24) - rightControlsReserve, Dpi.Px(6));
            using (GraphicsPath tp = Theme.CreateRoundedRectangle(trackRect, 3))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(16, 18, 22)))
            {
                g.FillPath(tb, tp);
            }

            double pct = Math.Max(0, Math.Min(100, item.ProgressPercent));
            int fillW = (int)(trackRect.Width * (pct / 100.0));
            if (fillW > 4)
            {
                Rectangle fillRect = new Rectangle(trackRect.X, trackRect.Y, fillW, trackRect.Height);
                using (GraphicsPath fp = Theme.CreateRoundedRectangle(fillRect, 3))
                using (SolidBrush fb = new SolidBrush(statusColor))
                {
                    g.FillPath(fb, fp);
                }
            }

            // Pause / Resume Button on Right (Next to Cancel button)
            if (hasPauseBtn)
            {
                Rectangle pauseRect = GetPauseResumeButtonRect(rect);
                bool isPaused = (item.Status == QueueItemStatus.Paused);
                Color btnBg = isPaused ? Color.FromArgb(26, 48, 34) : Color.FromArgb(46, 40, 26);
                Color iconCol = isPaused ? Theme.AccentSuccess : Theme.AccentWarning;

                using (GraphicsPath pp = Theme.CreateRoundedRectangle(pauseRect, 5))
                using (SolidBrush pb = new SolidBrush(btnBg))
                {
                    g.FillPath(pb, pp);
                }

                using (SolidBrush ib = new SolidBrush(iconCol))
                {
                    int cx = pauseRect.X + pauseRect.Width / 2;
                    int cy = pauseRect.Y + pauseRect.Height / 2;
                    if (isPaused)
                    {
                        // Crisp Play / Resume Triangle (▶)
                        Point[] tri = new Point[]
                        {
                            new Point(cx - Dpi.Px(3), cy - Dpi.Px(5)),
                            new Point(cx - Dpi.Px(3), cy + Dpi.Px(5)),
                            new Point(cx + Dpi.Px(5), cy)
                        };
                        g.FillPolygon(ib, tri);
                    }
                    else
                    {
                        // Crisp Pause Bars (❚❚)
                        g.FillRectangle(ib, cx - 4, cy - 5, 3, 10);
                        g.FillRectangle(ib, cx + 1, cy - 5, 3, 10);
                    }
                }
            }

            // Quick Action Button on Right (Cancel '✕' for active/paused, Open Folder '↗' for completed)
            Rectangle actRect = GetActionButtonRect(rect);
            if (CanCancelStatus(item.Status))
            {
                using (GraphicsPath ap = Theme.CreateRoundedRectangle(actRect, 5))
                using (SolidBrush ab = new SolidBrush(Color.FromArgb(48, 28, 32)))
                {
                    g.FillPath(ab, ap);
                }
                using (Font fAct = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "✕", fAct, actRect, Theme.AccentDanger,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            else if (item.Status == QueueItemStatus.Completed)
            {
                using (GraphicsPath ap = Theme.CreateRoundedRectangle(actRect, 5))
                using (SolidBrush ab = new SolidBrush(Color.FromArgb(26, 48, 34)))
                {
                    g.FillPath(ab, ap);
                }
                using (Font fAct = new Font("Segoe UI", 9F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "↗", fAct, actRect, Theme.AccentSuccess,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
