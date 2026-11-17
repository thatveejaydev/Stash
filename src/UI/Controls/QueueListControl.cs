using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Stash
{
    // Virtual Smooth Queue Canvas for modern card-like rows
    public partial class QueueListControl : Control
    {
        private readonly List<DownloadQueueItem> items = new List<DownloadQueueItem>();
        private sealed class CardSnapshot
        {
            public string Title, Badge, StatusText, Speed, Eta;
            public QueueItemStatus Status;
            public DownloadMode Mode;
            public double Progress;
            public int Height;
            public CompletedDownloadDetails Details;

            public CardSnapshot(DownloadQueueItem item, int height)
            {
                Title = item.Title; Badge = item.BadgeLabel; StatusText = item.StatusText;
                Speed = item.Speed; Eta = item.Eta; Status = item.Status;
                Mode = item.Mode; Progress = item.ProgressPercent; Height = height;
                Details = item.CompletedDetails;
            }

            public bool Matches(DownloadQueueItem item)
            {
                return Title == item.Title && Badge == item.BadgeLabel && StatusText == item.StatusText
                    && Speed == item.Speed && Eta == item.Eta && Status == item.Status
                    && Mode == item.Mode && Progress == item.ProgressPercent && Details == item.CompletedDetails;
            }
        }
        private readonly Dictionary<DownloadQueueItem, CardSnapshot> snapshots = new Dictionary<DownloadQueueItem, CardSnapshot>();
        public DownloadQueueItem SelectedItem { get; private set; }
        private DownloadQueueItem hoveredItem = null;
        private DarkScrollBar scrollBar;
        private static readonly int RowHeight = Dpi.Px(112);
        private static readonly int RowGap = Dpi.Px(10);
        private readonly Timer progressAnimationTimer = new Timer { Interval = 25 };
        private readonly Dictionary<DownloadQueueItem, double> displayedProgress = new Dictionary<DownloadQueueItem, double>();
        private int progressStripeOffset;

        public event EventHandler SelectionChanged;
        public event Action<DownloadQueueItem> PauseResumeRequested;
        public event Action<DownloadQueueItem> CancelRequested;
        public event Action<DownloadQueueItem> DetailsRequested;
        public event Action<DownloadQueueItem> RetryRequested;
        public event Action<DownloadQueueItem> RemoveRequested;
        public event Action<DownloadQueueItem> OpenFileFolderRequested;
        public event Action<DownloadQueueItem> OpenFileRequested;
        public event Action<DownloadQueueItem> CopyUrlRequested;

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
            progressAnimationTimer.Tick += AnimateProgressBars;
        }

        public void InsertItemsAtTop(IList<DownloadQueueItem> newItems)
        {
            if (newItems == null || newItems.Count == 0) return;
            for (int i = newItems.Count - 1; i >= 0; i--)
            {
                items.Insert(0, newItems[i]);
            }
            SelectedItem = newItems[0];
            UpdateScrollBar();
            if (scrollBar.Visible) scrollBar.Value = 0;
            Invalidate();
        }

        internal void AppendItems(IList<DownloadQueueItem> restored)
        {
            if (restored == null || restored.Count == 0) return;
            items.AddRange(restored);
            UpdateScrollBar();
            Invalidate();
        }

        public void RemoveItem(DownloadQueueItem item)
        {
            items.Remove(item);
            snapshots.Remove(item);
            displayedProgress.Remove(item);
            ReleaseThumbnail(item);
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

        public void RefreshChangedItems()
        {
            int y = -scrollBar.Value + Dpi.Px(4);
            bool layoutChanged = false;
            foreach (DownloadQueueItem item in items)
            {
                RefreshThumbnail(item);
                if (item.Status == QueueItemStatus.Completed || item.Status == QueueItemStatus.Cancelled || item.Status == QueueItemStatus.Error)
                    displayedProgress.Remove(item);
                else if (item.Status == QueueItemStatus.Paused)
                    displayedProgress[item] = GetProgressPercent(item);
                int height = GetRowHeight(item);
                CardSnapshot previous;
                bool known = snapshots.TryGetValue(item, out previous);
                if (known && previous.Height != height) layoutChanged = true;
                if (!known || !previous.Matches(item))
                {
                    snapshots[item] = new CardSnapshot(item, height);
                    Rectangle bounds = Rectangle.Intersect(ClientRectangle,
                        new Rectangle(0, y, ClientSize.Width, height));
                    if (!bounds.IsEmpty) Invalidate(bounds);
                }
                y += height + RowGap;
            }
            if (layoutChanged)
            {
                // Completed cards shrink, shifting all subsequent rows and the scrollbar.
                UpdateScrollBar();
                Invalidate();
            }
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

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            DownloadQueueItem hit = HitTestItem(e.Location);
            if (hit != null && e.Button == MouseButtons.Left && hit.Status == QueueItemStatus.Completed) Raise(OpenFileRequested, hit);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

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

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End:
                case Keys.PageUp: case Keys.PageDown: case Keys.Enter: case Keys.Space:
                case Keys.Delete: case Keys.Apps:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0) return;
            int index = SelectedItem == null ? -1 : items.IndexOf(SelectedItem);
            int page = Math.Max(1, ClientSize.Height / (RowHeight + RowGap));
            int target = index;
            switch (e.KeyCode)
            {
                case Keys.Up: target = Math.Max(0, index - 1); break;
                case Keys.Down: target = Math.Min(items.Count - 1, index + 1); break;
                case Keys.PageUp: target = Math.Max(0, index - page); break;
                case Keys.PageDown: target = Math.Min(items.Count - 1, index + page); break;
                case Keys.Home: target = 0; break;
                case Keys.End: target = items.Count - 1; break;
            }
            if (target != index && target >= 0)
            {
                Select(items[target]);
                e.Handled = true;
                return;
            }
            DownloadQueueItem item = SelectedItem;
            if (item == null) return;
            if (e.KeyCode == Keys.Space && CanPauseOrResume(item.Status)) Raise(PauseResumeRequested, item);
            else if (e.KeyCode == Keys.Enter && item.Status == QueueItemStatus.Completed) Raise(OpenFileRequested, item);
            else if (e.KeyCode == Keys.Enter && item.Status == QueueItemStatus.Error) Raise(DetailsRequested, item);
            else if (e.KeyCode == Keys.Delete && CanCancelStatus(item.Status)) Raise(CancelRequested, item);
            else if (e.KeyCode == Keys.Delete) Raise(RemoveRequested, item);
            else if (e.KeyCode == Keys.C && e.Control) Raise(CopyUrlRequested, item);
            else if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift))
            {
                Rectangle bounds = GetItemBounds(item);
                ShowContextMenu(item, new Point(bounds.X + Dpi.Px(24), Math.Max(0, bounds.Y) + Dpi.Px(24)));
            }
            else return;
            e.Handled = true;
            Invalidate();
        }

        private static void Raise(Action<DownloadQueueItem> handler, DownloadQueueItem item)
        {
            if (handler != null) handler(item);
        }

        private void Select(DownloadQueueItem item)
        {
            SelectedItem = item;
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            EnsureVisible(item);
            Invalidate();
        }

        private Rectangle GetItemBounds(DownloadQueueItem item)
        {
            int y = -scrollBar.Value + Dpi.Px(4);
            foreach (DownloadQueueItem candidate in items)
            {
                int height = GetRowHeight(candidate);
                if (candidate == item) return new Rectangle(Dpi.Px(2), y, GetCardWidth(), height);
                y += height + RowGap;
            }
            return Rectangle.Empty;
        }

        private void EnsureVisible(DownloadQueueItem item)
        {
            if (!scrollBar.Visible) return;
            Rectangle bounds = GetItemBounds(item);
            if (bounds.Top < 0) scrollBar.Value += bounds.Top - Dpi.Px(4);
            else if (bounds.Bottom > ClientSize.Height) scrollBar.Value += bounds.Bottom - ClientSize.Height + Dpi.Px(4);
        }

        private void ShowContextMenu(DownloadQueueItem item, Point location)
        {
            DarkContextMenu menu = new DarkContextMenu();
            QueueItemStatus status = item.Status;
            if (status == QueueItemStatus.Completed)
            {
                menu.AddItem("Open file", "Enter", true, () => Raise(OpenFileRequested, item));
                menu.AddItem("Show in folder", null, true, () => Raise(OpenFileFolderRequested, item));
            }
            if (CanPauseOrResume(status))
                menu.AddItem(status == QueueItemStatus.Paused ? "Resume" : "Pause", "Space", true, () => Raise(PauseResumeRequested, item));
            if (status == QueueItemStatus.Error || status == QueueItemStatus.Cancelled)
                menu.AddItem("Retry", null, true, () => Raise(RetryRequested, item));
            if (status == QueueItemStatus.Error)
                menu.AddItem("Show error details", "Enter", true, () => Raise(DetailsRequested, item));
            if (status != QueueItemStatus.Completed)
                menu.AddItem("Open download folder", null, true, () => Raise(OpenFileFolderRequested, item));
            menu.AddSeparator();
            menu.AddItem("Copy link", "Ctrl+C", true, () => Raise(CopyUrlRequested, item));
            menu.AddSeparator();
            if (CanCancelStatus(status))
                menu.AddItem("Cancel download", "Del", true, () => Raise(CancelRequested, item));
            else if (status != QueueItemStatus.Merging)
                menu.AddItem("Remove from list", "Del", true, () => Raise(RemoveRequested, item));
            menu.Show(this, location);
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
                if (e.Button == MouseButtons.Right)
                {
                    Invalidate();
                    ShowContextMenu(hit, e.Location);
                    return;
                }
                if (e.Button != MouseButtons.Left) return;

                if (hit.Status == QueueItemStatus.Error)
                {
                    for (int action = 0; action < 3; action++)
                    {
                        if (!GetErrorActionRect(rowRect, action).Contains(e.Location)) continue;
                        if (action == 0 && DetailsRequested != null) DetailsRequested(hit);
                        if (action == 1 && RetryRequested != null) RetryRequested(hit);
                        if (action == 2 && RemoveRequested != null) RemoveRequested(hit);
                        return;
                    }
                }

                if (hit.Status == QueueItemStatus.Cancelled)
                {
                    if (GetErrorActionRect(rowRect, 1).Contains(e.Location) && RetryRequested != null) RetryRequested(hit);
                    else if (GetErrorActionRect(rowRect, 2).Contains(e.Location) && RemoveRequested != null) RemoveRequested(hit);
                    return;
                }

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

        private int GetCardWidth()
        {
            int rightPad = scrollBar.Visible ? scrollBar.Width + Dpi.Px(6) : Dpi.Px(2);
            return ClientSize.Width - rightPad - Dpi.Px(2);
        }

        private DownloadQueueItem HitTestItemDetailed(Point pt, out int hitIndex, out Rectangle hitRect)
        {
            hitIndex = -1;
            hitRect = Rectangle.Empty;
            int yOffset = -scrollBar.Value + Dpi.Px(4);
            int width = GetCardWidth();

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
            int width = GetCardWidth();

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle rowRect = new Rectangle(Dpi.Px(2), yOffset, width, GetRowHeight(items[i]));
                yOffset += rowRect.Height + RowGap;
                if (rowRect.Top > e.ClipRectangle.Bottom) break;
                if (!rowRect.IntersectsWith(e.ClipRectangle)) continue;
                DrawQueueCard(g, items[i], rowRect);
            }
        }

        private void DrawEmptyState(Graphics g)
        {
            string title = "Ready to Download";
            string sub = "Copy any video or audio link and click 'Paste Link' (or press Ctrl+V anywhere in this window).";
            string credit = "Developed by Vee Jay";
            Font fTitle = Theme.GetFont("Segoe UI Semibold", 11.5F, FontStyle.Bold);
            Font fSub = Theme.GetFont("Segoe UI", 9.5F, FontStyle.Regular);
            Font fCredit = Theme.GetFont("Segoe UI", 8.5F, FontStyle.Regular);
            {
                Rectangle r1 = new Rectangle(0, ClientSize.Height / 2 - Dpi.Px(36), ClientSize.Width, Dpi.Px(26));
                Rectangle r2 = new Rectangle(0, ClientSize.Height / 2 - Dpi.Px(8), ClientSize.Width, Dpi.Px(24));
                Rectangle r3 = new Rectangle(0, ClientSize.Height / 2 + Dpi.Px(18), ClientSize.Width, Dpi.Px(22));
                TextRenderer.DrawText(g, title, fTitle, r1, Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, sub, fSub, r2, Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, credit, fCredit, r3, Color.FromArgb(92, 98, 114), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

    }
}
