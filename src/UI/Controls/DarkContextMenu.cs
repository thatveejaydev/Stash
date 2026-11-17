using System;
using System.Drawing;
using System.Windows.Forms;

namespace Stash
{
    // A context menu that matches the dark theme.
    internal sealed class DarkContextMenu : ContextMenuStrip
    {
        public DarkContextMenu()
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
            BackColor = Theme.BgSurface;
            ForeColor = Theme.TextPrimary;
            Font = Theme.GetFont("Segoe UI", 9.2F);
            ShowImageMargin = false;
            // Menus are rebuilt per use; release the native handles once closed.
            Closed += (s, e) =>
            {
                if (IsDisposed) return;
                Control owner = SourceControl;
                if (owner != null && owner.IsHandleCreated) owner.BeginInvoke((Action)Dispose);
                else Dispose();
            };
        }

        public ToolStripMenuItem AddItem(string text, string shortcut, bool enabled, Action onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text)
            {
                ShortcutKeyDisplayString = shortcut,
                Enabled = enabled,
                ForeColor = enabled ? Theme.TextPrimary : Theme.TextMuted
            };
            item.Click += (s, e) => onClick();
            Items.Add(item);
            return item;
        }

        public void AddSeparator()
        {
            if (Items.Count > 0 && !(Items[Items.Count - 1] is ToolStripSeparator)) Items.Add(new ToolStripSeparator());
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.BgSurface; } }
            public override Color MenuBorder { get { return Theme.BorderHover; } }
            public override Color MenuItemBorder { get { return Theme.BgSurfaceHover; } }
            public override Color MenuItemSelected { get { return Theme.BgSurfaceHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.BgSurfaceHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.BgSurfaceHover; } }
            public override Color SeparatorDark { get { return Theme.BorderSubtle; } }
            public override Color SeparatorLight { get { return Theme.BorderSubtle; } }
            public override Color ImageMarginGradientBegin { get { return Theme.BgSurface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.BgSurface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.BgSurface; } }
        }
    }
}
