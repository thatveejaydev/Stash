using System;
using System.Drawing;
using System.Windows.Forms;

namespace MediaDownloader
{
    public class DarkScrollBar : Control
    {
        private int maximum, largeChange = 1, current;
        private int dragOffset = -1;
        private bool hovered;
        public event EventHandler Scroll;
        public int SmallChange { get; set; }
        public int Maximum { get { return maximum; } set { maximum = Math.Max(0, value); Value = current; Invalidate(); } }
        public int LargeChange { get { return largeChange; } set { largeChange = Math.Max(1, value); Value = current; Invalidate(); } }
        private int Limit { get { return Math.Max(0, maximum - largeChange + 1); } }
        public int Value
        {
            get { return current; }
            set
            {
                int next = Math.Max(0, Math.Min(Limit, value));
                if (next == current) return;
                current = next; Invalidate();
                if (Scroll != null) Scroll(this, EventArgs.Empty);
            }
        }
        public DarkScrollBar()
        {
            Width = 10; SmallChange = 30; BackColor = Theme.BgWindow;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        private Rectangle Thumb
        {
            get
            {
                int track = Math.Max(1, Height - 4);
                int height = Math.Min(track, Math.Max(24, (int)((double)track * largeChange / Math.Max(largeChange, maximum + 1))));
                int y = 2 + (Limit == 0 ? 0 : (int)((double)current / Limit * (track - height)));
                return new Rectangle(2, y, Math.Max(1, Width - 4), height);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Theme.CreateRoundedRectangle(Thumb, 3))
            using (var brush = new SolidBrush(hovered || dragOffset >= 0 ? Theme.TextMuted : Theme.BorderHover))
                e.Graphics.FillPath(brush, path);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location)) { dragOffset = e.Y - thumb.Y; Capture = true; }
            else Value += e.Y < thumb.Y ? -LargeChange : LargeChange;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragOffset >= 0)
            {
                int travel = Height - 4 - Thumb.Height;
                if (travel > 0) Value = (int)((double)(e.Y - dragOffset - 2) * Limit / travel);
            }
            hovered = true; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragOffset = -1; Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragOffset = -1; }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Value -= e.Delta / 120 * SmallChange; }
    }
}
