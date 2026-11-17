using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Stash
{
    // Right-click menu drawn in the same style as the settings popups: a bordered surface,
    // roomy rows with rounded hover highlights, muted shortcuts and thin group separators.
    internal sealed class DarkContextMenu
    {
        private sealed class MenuEntry
        {
            public string Text;
            public string Shortcut;
            public bool Enabled;
            public bool Danger;
            public bool Separator;
            public Action Run;
            public Rectangle Bounds;
        }

        private readonly List<MenuEntry> entries = new List<MenuEntry>();

        public void AddItem(string text, string shortcut, bool enabled, Action onClick, bool danger = false)
        {
            entries.Add(new MenuEntry { Text = text, Shortcut = shortcut, Enabled = enabled, Run = onClick, Danger = danger });
        }

        public void AddSeparator()
        {
            if (entries.Count > 0 && !entries[entries.Count - 1].Separator) entries.Add(new MenuEntry { Separator = true });
        }

        public void Show(Control owner, Point location)
        {
            while (entries.Count > 0 && entries[entries.Count - 1].Separator) entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0) return;

            MenuSurface surface = new MenuSurface(entries);
            ToolStripDropDown popup = BufferedDropDown.Create(surface, true);
            surface.Chosen += entry =>
            {
                popup.Close(ToolStripDropDownCloseReason.ItemClicked);
                // Run after the popup has closed so dialogs opened by the action get focus.
                if (owner.IsHandleCreated) owner.BeginInvoke(entry.Run);
                else entry.Run();
            };
            popup.Closed += (s, e) =>
            {
                // Disposing inside Closed breaks WinForms' menu message filter, so defer it. If the owner
                // is closing too (the window closed with the menu open), leave it to the garbage collector.
                try
                {
                    if (owner.IsHandleCreated && !owner.IsDisposed && !owner.Disposing) owner.BeginInvoke((Action)popup.Dispose);
                }
                catch (InvalidOperationException) { }
            };
            popup.Show(owner, location);
            surface.Focus();
        }

        private sealed class MenuSurface : Control
        {
            private static readonly int Inset = Dpi.Px(5);
            private static readonly int RowHeight = Dpi.Px(32);
            private static readonly int SeparatorHeight = Dpi.Px(9);
            private readonly List<MenuEntry> entries;
            private int hovered = -1;

            public event Action<MenuEntry> Chosen;

            public MenuSurface(List<MenuEntry> entries)
            {
                this.entries = entries;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
                BackColor = Theme.BgSurface;
                Font = Theme.GetFont("Segoe UI", 9.25F);
                Cursor = Cursors.Hand;

                int textWidth = 0, shortcutWidth = 0;
                foreach (MenuEntry entry in entries)
                {
                    if (entry.Separator) continue;
                    textWidth = Math.Max(textWidth, TextRenderer.MeasureText(entry.Text, Font).Width);
                    if (!string.IsNullOrEmpty(entry.Shortcut))
                        shortcutWidth = Math.Max(shortcutWidth, TextRenderer.MeasureText(entry.Shortcut, ShortcutFont).Width);
                }
                int width = Math.Max(Dpi.Px(200), Inset * 2 + Dpi.Px(14) + textWidth + (shortcutWidth > 0 ? Dpi.Px(32) + shortcutWidth : 0) + Dpi.Px(14));
                int y = Inset;
                foreach (MenuEntry entry in entries)
                {
                    int height = entry.Separator ? SeparatorHeight : RowHeight;
                    entry.Bounds = new Rectangle(Inset, y, width - Inset * 2, height);
                    y += height;
                }
                Size = new Size(width, y + Inset);
            }

            private static Font ShortcutFont { get { return Theme.GetFont("Segoe UI", 8.5F); } }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                return key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { MoveSelection(e.KeyCode == Keys.Down ? 1 : -1); e.Handled = true; }
                else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && hovered >= 0) { Choose(hovered); e.Handled = true; }
            }

            private void MoveSelection(int direction)
            {
                int index = hovered;
                for (int step = 0; step < entries.Count; step++)
                {
                    index = (index + direction + entries.Count) % entries.Count;
                    if (!entries[index].Separator && entries[index].Enabled) { hovered = index; Invalidate(); return; }
                }
            }

            private int HitTest(Point point)
            {
                for (int i = 0; i < entries.Count; i++)
                    if (!entries[i].Separator && entries[i].Enabled && entries[i].Bounds.Contains(point)) return i;
                return -1;
            }

            private void Choose(int index)
            {
                if (index < 0 || Chosen == null) return;
                Chosen(entries[index]);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int hit = HitTest(e.Location);
                if (hit != hovered) { hovered = hit; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hovered >= 0) { hovered = -1; Invalidate(); }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left) Choose(HitTest(e.Location));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(Theme.BgSurface);
                using (Pen border = new Pen(Theme.BorderHover)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                for (int i = 0; i < entries.Count; i++)
                {
                    MenuEntry entry = entries[i];
                    Rectangle bounds = entry.Bounds;
                    if (entry.Separator)
                    {
                        int y = bounds.Y + bounds.Height / 2;
                        using (Pen line = new Pen(Theme.BorderSubtle)) g.DrawLine(line, bounds.X + Dpi.Px(8), y, bounds.Right - Dpi.Px(8), y);
                        continue;
                    }
                    if (i == hovered)
                        using (GraphicsPath path = Theme.CreateRoundedRectangle(bounds, Dpi.Px(6)))
                        using (SolidBrush fill = new SolidBrush(Theme.BgSurfaceHover)) g.FillPath(fill, path);

                    Color textColor = !entry.Enabled ? Theme.TextMuted : entry.Danger ? Theme.DangerText : Theme.TextPrimary;
                    Rectangle text = new Rectangle(bounds.X + Dpi.Px(12), bounds.Y, bounds.Width - Dpi.Px(24), bounds.Height);
                    TextRenderer.DrawText(g, entry.Text, Font, text, textColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    if (!string.IsNullOrEmpty(entry.Shortcut))
                        TextRenderer.DrawText(g, entry.Shortcut, ShortcutFont, text, Theme.TextMuted,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
