using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MediaDownloader
{
    // Compact Minimalist Pill Toggle Switch (Smaller, no lightbulb icon)
    public class CompactToggleSwitch : Control
    {
        private bool isChecked = true;
        private bool isHovered = false;
        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public CompactToggleSwitch()
        {
            Size = new Size(Dpi.Px(38), Dpi.Px(20));
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Checked = !Checked;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgTopBar;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            Rectangle pillRect = new Rectangle(0, 0, Width - Dpi.Px(1), Height - Dpi.Px(1));
            Color trackColor = isChecked
                ? (isHovered ? Theme.BrandPurpleHover : Theme.BrandPurple)
                : (isHovered ? Color.FromArgb(68, 72, 84) : Color.FromArgb(52, 56, 66));

            using (GraphicsPath path = Theme.CreateRoundedRectangle(pillRect, pillRect.Height / 2))
            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                g.FillPath(trackBrush, path);
            }

            int knobSize = pillRect.Height - 4;
            int knobX = isChecked ? (pillRect.Right - knobSize - 2) : (pillRect.X + 2);
            int knobY = pillRect.Y + 2;
            Rectangle knobRect = new Rectangle(knobX, knobY, knobSize, knobSize);

            using (SolidBrush knobBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(knobBrush, knobRect);
            }
        }
    }
}
