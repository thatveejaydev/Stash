using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MediaDownloader
{
    public class DarkRichTextScrollHost : Panel
    {
        [StructLayout(LayoutKind.Sequential)] private struct ScrollPoint { public int X, Y; }
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref ScrollPoint point);
        private readonly RichTextBox editor;
        private readonly DarkScrollBar bar;
        private bool syncing;
        public DarkRichTextScrollHost(RichTextBox textBox)
        {
            editor = textBox; Dock = DockStyle.Fill; BackColor = editor.BackColor;
            editor.ScrollBars = RichTextBoxScrollBars.None;
            editor.WordWrap = true;
            bar = new DarkScrollBar { Dock = DockStyle.Right, Visible = false, BackColor = editor.BackColor };
            Controls.Add(editor); Controls.Add(bar);
            bar.Scroll += (s, e) =>
            {
                if (syncing || !editor.IsHandleCreated) return;
                ScrollPoint position = new ScrollPoint { Y = bar.Value };
                SendMessage(editor.Handle, 0x04DE, IntPtr.Zero, ref position); // EM_SETSCROLLPOS
                SyncScroll();
            };
            editor.TextChanged += (s, e) => ScheduleSync();
            editor.VScroll += (s, e) => ScheduleSync();
            editor.Resize += (s, e) => ScheduleSync();
            editor.KeyUp += (s, e) => ScheduleSync();
            editor.MouseWheel += (s, e) => ScheduleSync();
            VisibleChanged += (s, e) => ScheduleSync();
        }
        private void ScheduleSync()
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)SyncScroll);
        }
        private void SyncScroll()
        {
            if (syncing || IsDisposed || !editor.IsHandleCreated) return;
            syncing = true;
            try
            {
                ScrollPoint position = new ScrollPoint();
                SendMessage(editor.Handle, 0x04DD, IntPtr.Zero, ref position); // EM_GETSCROLLPOS
                int extent = editor.GetPositionFromCharIndex(editor.TextLength).Y + position.Y + editor.Font.Height + 4;
                bar.Maximum = Math.Max(0, extent);
                bar.LargeChange = Math.Max(1, editor.ClientSize.Height);
                bar.SmallChange = editor.Font.Height * 3;
                bar.Value = position.Y;
                bar.Visible = extent > editor.ClientSize.Height;
            }
            finally { syncing = false; }
        }
    }
}
