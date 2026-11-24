using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Stash
{
    internal enum QueueSectionAction { PauseAll, ResumeAll, ClearCompleted }

    // Virtual queue canvas: an "In progress" section followed by a "Completed" section.
    // Rows are painted directly; actions appear as icon buttons on each card.
    public partial class QueueListControl : Control
    {
        private enum CardAction { None, PauseResume, Cancel, Retry, Remove, Details, OpenFile, OpenFolder, More }

        private sealed class Entry
        {
            public bool IsHeader;
            public bool Completed;
            public DownloadQueueItem Item;
            public int Top;
            public int Height;
            public int Count;
        }

        private sealed class CardSnapshot
        {
            public string Title, StatusText, Speed, Eta;
            public QueueItemStatus Status;
            public double Progress;
            public CompletedDownloadDetails Details;
            public DownloadFailure Failure;

            public CardSnapshot(DownloadQueueItem item)
            {
                Title = item.Title; StatusText = item.StatusText; Speed = item.Speed; Eta = item.Eta;
                Status = item.Status; Progress = item.ProgressPercent; Details = item.CompletedDetails; Failure = item.Failure;
            }

            public bool Matches(DownloadQueueItem item)
            {
                return Title == item.Title && StatusText == item.StatusText && Speed == item.Speed && Eta == item.Eta &&
                    Status == item.Status && Progress == item.ProgressPercent && Details == item.CompletedDetails && Failure == item.Failure;
            }
        }

        private static readonly int HeaderHeight = Dpi.Px(30);
        private static readonly int ActiveHeight = Dpi.Px(76);
        private static readonly int CompletedHeight = Dpi.Px(60);
        private static readonly int ActiveGap = Dpi.Px(8);
        private static readonly int CompletedGap = Dpi.Px(2);
        private static readonly int SectionGap = Dpi.Px(14);
        private static readonly int ActionSize = Dpi.Px(30);

        private readonly List<DownloadQueueItem> items = new List<DownloadQueueItem>();
        private readonly Dictionary<DownloadQueueItem, CardSnapshot> snapshots = new Dictionary<DownloadQueueItem, CardSnapshot>();
        private readonly DarkScrollBar scrollBar;
        private readonly Timer progressAnimationTimer = new Timer { Interval = 25 };
        private readonly Dictionary<DownloadQueueItem, double> displayedProgress = new Dictionary<DownloadQueueItem, double>();
        private readonly ToolTip actionTip = new ToolTip { InitialDelay = 500 };
        private int progressStripeOffset;
        private DownloadQueueItem hoveredItem;
        private CardAction hoveredAction;
        private int hoveredHeaderLink = -1; // 0 = in progress, 1 = completed
        private string currentTip;
        private bool dropHighlight;

        public DownloadQueueItem SelectedItem { get; private set; }

        public event EventHandler SelectionChanged;
        public event Action<DownloadQueueItem> PauseResumeRequested;
        public event Action<DownloadQueueItem> CancelRequested;
        public event Action<DownloadQueueItem> DetailsRequested;
        public event Action<DownloadQueueItem> RetryRequested;
        public event Action<DownloadQueueItem> RemoveRequested;
        public event Action<DownloadQueueItem> OpenFileFolderRequested;
        public event Action<DownloadQueueItem> OpenFileRequested;
        public event Action<DownloadQueueItem> CopyUrlRequested;
        public event Action<DownloadQueueItem> RenameRequested;
        internal event Action<QueueSectionAction> SectionActionRequested;

        public QueueListControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.BgWindow;
            scrollBar = new DarkScrollBar { Dock = DockStyle.Right, Visible = false, SmallChange = ActiveHeight / 2, LargeChange = ActiveHeight * 2 };
            scrollBar.Scroll += (s, e) => Invalidate();
            Controls.Add(scrollBar);
            progressAnimationTimer.Tick += AnimateProgressBars;
        }

        internal bool DropHighlight
        {
            get { return dropHighlight; }
            set { if (dropHighlight == value) return; dropHighlight = value; Invalidate(); }
        }

        // ---- Item management ----

        public void InsertItemsAtTop(IList<DownloadQueueItem> newItems)
        {
            if (newItems == null || newItems.Count == 0) return;
            for (int i = newItems.Count - 1; i >= 0; i--) items.Insert(0, newItems[i]);
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
            List<DownloadQueueItem> order = GetDisplayOrder();
            int index = order.IndexOf(item);
            items.Remove(item);
            snapshots.Remove(item);
            displayedProgress.Remove(item);
            ReleaseThumbnail(item);
            if (hoveredItem == item) hoveredItem = null;
            if (SelectedItem == item)
            {
                order.Remove(item);
                SelectedItem = order.Count == 0 ? null : order[Math.Min(Math.Max(0, index), order.Count - 1)];
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
            bool relayout = false;
            foreach (DownloadQueueItem item in items)
            {
                RefreshThumbnail(item);
                if (item.Status == QueueItemStatus.Completed || item.Status == QueueItemStatus.Cancelled || item.Status == QueueItemStatus.Error)
                    displayedProgress.Remove(item);
                else if (item.Status == QueueItemStatus.Paused)
                    displayedProgress[item] = GetProgressPercent(item);
                CardSnapshot previous;
                bool known = snapshots.TryGetValue(item, out previous);
                if (known && previous.Matches(item)) continue;
                // Moving between sections changes every row below it.
                if (!known || (previous.Status == QueueItemStatus.Completed) != (item.Status == QueueItemStatus.Completed)) relayout = true;
                snapshots[item] = new CardSnapshot(item);
                if (!relayout)
                {
                    Rectangle bounds = GetItemBounds(item);
                    if (!bounds.IsEmpty) Invalidate(Rectangle.Intersect(ClientRectangle, bounds));
                }
            }
            if (relayout)
            {
                UpdateScrollBar();
                Invalidate();
            }
        }

        // ---- Layout ----

        private List<DownloadQueueItem> GetDisplayOrder()
        {
            List<DownloadQueueItem> order = items.FindAll(item => item.Status != QueueItemStatus.Completed);
            order.AddRange(items.FindAll(item => item.Status == QueueItemStatus.Completed));
            return order;
        }

        private List<Entry> BuildLayout()
        {
            List<Entry> layout = new List<Entry>();
            List<DownloadQueueItem> active = items.FindAll(item => item.Status != QueueItemStatus.Completed);
            List<DownloadQueueItem> done = items.FindAll(item => item.Status == QueueItemStatus.Completed);
            int y = Dpi.Px(2);
            if (active.Count > 0)
            {
                layout.Add(new Entry { IsHeader = true, Top = y, Height = HeaderHeight, Count = active.Count });
                y += HeaderHeight;
                foreach (DownloadQueueItem item in active)
                {
                    layout.Add(new Entry { Item = item, Top = y, Height = ActiveHeight });
                    y += ActiveHeight + ActiveGap;
                }
            }
            if (done.Count > 0)
            {
                if (active.Count > 0) y += SectionGap - ActiveGap;
                layout.Add(new Entry { IsHeader = true, Completed = true, Top = y, Height = HeaderHeight, Count = done.Count });
                y += HeaderHeight;
                foreach (DownloadQueueItem item in done)
                {
                    layout.Add(new Entry { Item = item, Completed = true, Top = y, Height = CompletedHeight });
                    y += CompletedHeight + CompletedGap;
                }
            }
            return layout;
        }

        private int ContentHeight(List<Entry> layout)
        {
            if (layout.Count == 0) return 0;
            Entry last = layout[layout.Count - 1];
            return last.Top + last.Height + Dpi.Px(8);
        }

        private int GetContentWidth()
        {
            return ClientSize.Width - (scrollBar.Visible ? scrollBar.Width + Dpi.Px(6) : 0);
        }

        private Rectangle ToClient(Entry entry)
        {
            return new Rectangle(0, entry.Top - scrollBar.Value, GetContentWidth(), entry.Height);
        }

        private Rectangle GetItemBounds(DownloadQueueItem item)
        {
            foreach (Entry entry in BuildLayout())
                if (entry.Item == item) return ToClient(entry);
            return Rectangle.Empty;
        }

        private void UpdateScrollBar()
        {
            int total = ContentHeight(BuildLayout());
            if (total > ClientSize.Height && ClientSize.Height > 0)
            {
                scrollBar.Maximum = total;
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

        private string GetSectionLink(bool completed)
        {
            if (completed) return "Clear";
            bool running = false, paused = false;
            foreach (DownloadQueueItem item in items)
            {
                if (item.Status == QueueItemStatus.Queued || DownloadManager.IsActive(item.Status)) running = true;
                else if (item.Status == QueueItemStatus.Paused) paused = true;
            }
            return running ? "Pause all" : paused ? "Resume all" : null;
        }

        private Rectangle GetSectionLinkRect(Rectangle header, string link)
        {
            if (link == null) return Rectangle.Empty;
            int width = TextRenderer.MeasureText(link, SectionFont).Width + Dpi.Px(8);
            return new Rectangle(header.Right - width - Dpi.Px(2), header.Y + Dpi.Px(4), width, header.Height - Dpi.Px(8));
        }

        // Icon buttons shown on a card, right to left.
        private List<KeyValuePair<CardAction, Rectangle>> GetCardActions(DownloadQueueItem item, Rectangle card)
        {
            List<CardAction> actions = new List<CardAction>();
            switch (item.Status)
            {
                case QueueItemStatus.Completed:
                    if (item == hoveredItem || (item == SelectedItem && Focused))
                    {
                        actions.Add(CardAction.More);
                        actions.Add(CardAction.OpenFolder);
                        actions.Add(CardAction.OpenFile);
                    }
                    break;
                case QueueItemStatus.Error:
                    actions.Add(CardAction.Remove);
                    actions.Add(CardAction.Retry);
                    actions.Add(CardAction.Details);
                    break;
                case QueueItemStatus.Cancelled:
                    actions.Add(CardAction.Remove);
                    actions.Add(CardAction.Retry);
                    break;
                case QueueItemStatus.Merging:
                    actions.Add(CardAction.Cancel);
                    break;
                default:
                    actions.Add(CardAction.Cancel);
                    actions.Add(CardAction.PauseResume);
                    break;
            }
            List<KeyValuePair<CardAction, Rectangle>> result = new List<KeyValuePair<CardAction, Rectangle>>();
            int x = card.Right - Dpi.Px(10) - ActionSize;
            int y = card.Y + (card.Height - ActionSize) / 2;
            foreach (CardAction action in actions)
            {
                result.Add(new KeyValuePair<CardAction, Rectangle>(action, new Rectangle(x, y, ActionSize, ActionSize)));
                x -= ActionSize + Dpi.Px(4);
            }
            return result;
        }

        private static string DescribeAction(CardAction action, DownloadQueueItem item)
        {
            switch (action)
            {
                case CardAction.PauseResume: return item.Status == QueueItemStatus.Paused ? "Resume (Space)" : "Pause (Space)";
                case CardAction.Cancel: return "Cancel (Del)";
                case CardAction.Retry: return "Retry";
                case CardAction.Remove: return "Remove from list (Del)";
                case CardAction.Details: return "Error details (Enter)";
                case CardAction.OpenFile: return "Open file (Enter)";
                case CardAction.OpenFolder: return "Show in folder";
                case CardAction.More: return "More actions";
                default: return null;
            }
        }

        // ---- Hit testing ----

        private Entry HitTest(Point point, out Rectangle bounds)
        {
            foreach (Entry entry in BuildLayout())
            {
                Rectangle rect = ToClient(entry);
                if (rect.Contains(point)) { bounds = rect; return entry; }
            }
            bounds = Rectangle.Empty;
            return null;
        }

        private CardAction HitAction(DownloadQueueItem item, Rectangle card, Point point)
        {
            foreach (KeyValuePair<CardAction, Rectangle> action in GetCardActions(item, card))
                if (action.Value.Contains(point)) return action.Key;
            return CardAction.None;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Rectangle bounds;
            Entry entry = HitTest(e.Location, out bounds);
            DownloadQueueItem item = entry == null ? null : entry.Item;
            CardAction action = CardAction.None;
            int headerLink = -1;
            if (item != null)
            {
                DownloadQueueItem previousHover = hoveredItem;
                hoveredItem = item; // Completed cards reveal their buttons on hover.
                action = HitAction(item, bounds, e.Location);
                hoveredItem = previousHover;
            }
            else if (entry != null && entry.IsHeader && GetSectionLinkRect(bounds, GetSectionLink(entry.Completed)).Contains(e.Location))
            {
                headerLink = entry.Completed ? 1 : 0;
            }
            Cursor = action != CardAction.None || headerLink >= 0 ? Cursors.Hand : Cursors.Default;
            if (item != hoveredItem || action != hoveredAction || headerLink != hoveredHeaderLink)
            {
                hoveredItem = item;
                hoveredAction = action;
                hoveredHeaderLink = headerLink;
                string tip = action == CardAction.None ? null : DescribeAction(action, item);
                if (tip != currentTip) { currentTip = tip; actionTip.SetToolTip(this, tip); }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredItem != null || hoveredHeaderLink >= 0)
            {
                hoveredItem = null;
                hoveredAction = CardAction.None;
                hoveredHeaderLink = -1;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!scrollBar.Visible) return;
            scrollBar.Value = Math.Max(0, scrollBar.Value - e.Delta / 2);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Rectangle bounds;
            Entry entry = HitTest(e.Location, out bounds);
            if (entry == null) return;
            if (entry.IsHeader)
            {
                string link = GetSectionLink(entry.Completed);
                if (e.Button == MouseButtons.Left && GetSectionLinkRect(bounds, link).Contains(e.Location) && SectionActionRequested != null)
                    SectionActionRequested(entry.Completed ? QueueSectionAction.ClearCompleted
                        : link == "Resume all" ? QueueSectionAction.ResumeAll : QueueSectionAction.PauseAll);
                return;
            }
            DownloadQueueItem item = entry.Item;
            SelectedItem = item;
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            Invalidate();
            if (e.Button == MouseButtons.Right)
            {
                ShowContextMenu(item, e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            CardAction action = HitAction(item, bounds, e.Location);
            switch (action)
            {
                case CardAction.PauseResume: Raise(PauseResumeRequested, item); break;
                case CardAction.Cancel: Raise(CancelRequested, item); break;
                case CardAction.Retry: Raise(RetryRequested, item); break;
                case CardAction.Remove: Raise(RemoveRequested, item); break;
                case CardAction.Details: Raise(DetailsRequested, item); break;
                case CardAction.OpenFile: Raise(OpenFileRequested, item); break;
                case CardAction.OpenFolder: Raise(OpenFileFolderRequested, item); break;
                case CardAction.More:
                    foreach (KeyValuePair<CardAction, Rectangle> a in GetCardActions(item, bounds))
                        if (a.Key == CardAction.More) ShowContextMenu(item, new Point(a.Value.Left, a.Value.Bottom + Dpi.Px(2)));
                    break;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Rectangle bounds;
            Entry entry = HitTest(e.Location, out bounds);
            if (entry == null || entry.Item == null || e.Button != MouseButtons.Left) return;
            if (HitAction(entry.Item, bounds, e.Location) != CardAction.None) return;
            if (entry.Item.Status == QueueItemStatus.Completed) Raise(OpenFileRequested, entry.Item);
            else if (entry.Item.Status == QueueItemStatus.Error) Raise(DetailsRequested, entry.Item);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ---- Keyboard ----

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
            List<DownloadQueueItem> order = GetDisplayOrder();
            if (order.Count == 0) return;
            int index = SelectedItem == null ? -1 : order.IndexOf(SelectedItem);
            int page = Math.Max(1, ClientSize.Height / (ActiveHeight + ActiveGap));
            int target = index;
            switch (e.KeyCode)
            {
                case Keys.Up: target = Math.Max(0, index - 1); break;
                case Keys.Down: target = Math.Min(order.Count - 1, index + 1); break;
                case Keys.PageUp: target = Math.Max(0, index - page); break;
                case Keys.PageDown: target = Math.Min(order.Count - 1, index + page); break;
                case Keys.Home: target = 0; break;
                case Keys.End: target = order.Count - 1; break;
            }
            if (target != index && target >= 0)
            {
                Select(order[target]);
                e.Handled = true;
                return;
            }
            DownloadQueueItem item = SelectedItem;
            if (item == null) return;
            if (e.KeyCode == Keys.Space && CanPauseOrResume(item.Status)) Raise(PauseResumeRequested, item);
            else if (e.KeyCode == Keys.Enter && item.Status == QueueItemStatus.Completed) Raise(OpenFileRequested, item);
            else if (e.KeyCode == Keys.Enter && item.Status == QueueItemStatus.Error) Raise(DetailsRequested, item);
            else if (e.KeyCode == Keys.Delete && DownloadManager.CanCancel(item.Status)) Raise(CancelRequested, item);
            else if (e.KeyCode == Keys.Delete) Raise(RemoveRequested, item);
            else if (e.KeyCode == Keys.C && e.Control) Raise(CopyUrlRequested, item);
            else if (e.KeyCode == Keys.F2 && item.Status == QueueItemStatus.Completed) Raise(RenameRequested, item);
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

        private void EnsureVisible(DownloadQueueItem item)
        {
            if (!scrollBar.Visible) return;
            Rectangle bounds = GetItemBounds(item);
            if (bounds.Top < 0) scrollBar.Value += bounds.Top - Dpi.Px(4);
            else if (bounds.Bottom > ClientSize.Height) scrollBar.Value += bounds.Bottom - ClientSize.Height + Dpi.Px(4);
        }

        private static bool CanPauseOrResume(QueueItemStatus status)
        {
            return status == QueueItemStatus.Queued || status == QueueItemStatus.FetchingInfo ||
                status == QueueItemStatus.Downloading || status == QueueItemStatus.Paused;
        }

        private void ShowContextMenu(DownloadQueueItem item, Point location)
        {
            DarkContextMenu menu = new DarkContextMenu();
            QueueItemStatus status = item.Status;
            if (status == QueueItemStatus.Completed)
            {
                menu.AddItem("Open file", "Enter", true, () => Raise(OpenFileRequested, item));
                menu.AddItem("Show in folder", null, true, () => Raise(OpenFileFolderRequested, item));
                menu.AddItem("Rename file", "F2", true, () => Raise(RenameRequested, item));
            }
            if (CanPauseOrResume(status))
                menu.AddItem(status == QueueItemStatus.Paused ? "Resume" : "Pause", "Space", true, () => Raise(PauseResumeRequested, item));
            if (status == QueueItemStatus.Error || status == QueueItemStatus.Cancelled)
                menu.AddItem("Retry", null, true, () => Raise(RetryRequested, item));
            if (status == QueueItemStatus.Error)
                menu.AddItem("Error details", "Enter", true, () => Raise(DetailsRequested, item));
            if (status != QueueItemStatus.Completed)
                menu.AddItem("Open download folder", null, true, () => Raise(OpenFileFolderRequested, item));
            menu.AddSeparator();
            menu.AddItem("Copy link", "Ctrl+C", true, () => Raise(CopyUrlRequested, item));
            menu.AddSeparator();
            if (DownloadManager.CanCancel(status))
                menu.AddItem("Cancel download", "Del", true, () => Raise(CancelRequested, item), true);
            else
                menu.AddItem("Remove from list", "Del", true, () => Raise(RemoveRequested, item), true);
            menu.Show(this, location);
        }

        // ---- Painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush bg = new SolidBrush(Theme.BgWindow)) g.FillRectangle(bg, ClientRectangle);

            if (items.Count == 0)
            {
                DrawEmptyState(g);
                return;
            }

            foreach (Entry entry in BuildLayout())
            {
                Rectangle rect = ToClient(entry);
                if (rect.Top > e.ClipRectangle.Bottom) break;
                if (!rect.IntersectsWith(e.ClipRectangle)) continue;
                if (entry.IsHeader) DrawSectionHeader(g, entry, rect);
                else if (entry.Completed) DrawCompletedCard(g, entry.Item, rect);
                else DrawActiveCard(g, entry.Item, rect);
            }

            if (dropHighlight) DrawDropOutline(g, new Rectangle(Dpi.Px(1), Dpi.Px(1), ClientSize.Width - Dpi.Px(3), ClientSize.Height - Dpi.Px(3)));
        }

        private void DrawSectionHeader(Graphics g, Entry entry, Rectangle rect)
        {
            string title = (entry.Completed ? "Completed" : "In progress") + "  ·  " + entry.Count;
            TextRenderer.DrawText(g, title, SectionFont, new Rectangle(rect.X + Dpi.Px(2), rect.Y, rect.Width / 2, rect.Height),
                Theme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            string link = GetSectionLink(entry.Completed);
            if (link == null) return;
            Rectangle linkRect = GetSectionLinkRect(rect, link);
            bool hovered = hoveredHeaderLink == (entry.Completed ? 1 : 0);
            if (hovered)
                using (GraphicsPath path = Theme.CreateRoundedRectangle(linkRect, Dpi.Px(6)))
                using (SolidBrush fill = new SolidBrush(Theme.BgSurface)) g.FillPath(fill, path);
            TextRenderer.DrawText(g, link, SectionFont, linkRect, hovered ? Theme.TextPrimary : Theme.AccentText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawEmptyState(Graphics g)
        {
            int width = Math.Min(ClientSize.Width - Dpi.Px(8), Dpi.Px(520));
            int height = Math.Min(ClientSize.Height - Dpi.Px(48), Dpi.Px(190));
            if (width <= 0 || height <= 0) return;
            Rectangle zone = new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2 - Dpi.Px(14), width, height);
            DrawDropOutline(g, zone);
            Icons.Draw(g, IconKind.Download, new Rectangle(zone.X, zone.Y + zone.Height / 2 - Dpi.Px(56), zone.Width, Dpi.Px(32)),
                dropHighlight ? Theme.AccentText : Theme.TextMuted, 24f);
            TextRenderer.DrawText(g, "Paste or drop a link to start", Theme.GetFont("Segoe UI Semibold", 11.5F),
                new Rectangle(zone.X, zone.Y + zone.Height / 2 - Dpi.Px(14), zone.Width, Dpi.Px(26)), Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Ctrl+V works anywhere in this window.", Theme.GetFont("Segoe UI", 9.5F),
                new Rectangle(zone.X, zone.Y + zone.Height / 2 + Dpi.Px(12), zone.Width, Dpi.Px(22)), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Developed by Vee Jay", Theme.GetFont("Segoe UI", 8.5F),
                new Rectangle(0, zone.Bottom + Dpi.Px(12), ClientSize.Width, Dpi.Px(20)), Theme.BorderHover,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawDropOutline(Graphics g, Rectangle zone)
        {
            using (GraphicsPath path = Theme.CreateRoundedRectangle(zone, Dpi.Px(14)))
            using (Pen pen = new Pen(dropHighlight ? Theme.BorderFocus : Theme.BorderHover, dropHighlight ? 1.6f : 1.2f) { DashStyle = DashStyle.Dash })
            {
                if (dropHighlight)
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(18, Theme.BrandPurple))) g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                progressAnimationTimer.Dispose();
                actionTip.Dispose();
                foreach (CardThumbnail thumbnail in thumbnails.Values) thumbnail.Release();
                thumbnails.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
