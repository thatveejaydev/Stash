using System;
using System.Runtime.InteropServices;

namespace Stash
{
    // Shows queue progress on the Windows taskbar button (ITaskbarList3).
    internal sealed class TaskbarProgress
    {
        private ITaskbarList3 taskbar;
        private bool unavailable;
        private int lastState = -1;
        private ulong lastValue = ulong.MaxValue;

        private const int TbpfNoProgress = 0;
        private const int TbpfNormal = 0x2;
        private const int TbpfError = 0x4;

        internal void SetProgress(IntPtr window, double fraction, bool hasErrors)
        {
            ulong value = (ulong)Math.Round(Math.Max(0, Math.Min(1, fraction)) * 1000);
            Apply(window, hasErrors ? TbpfError : TbpfNormal, value);
        }

        internal void Clear(IntPtr window)
        {
            Apply(window, TbpfNoProgress, 0);
        }

        private void Apply(IntPtr window, int state, ulong value)
        {
            if (unavailable || window == IntPtr.Zero) return;
            if (state == lastState && value == lastValue) return;
            try
            {
                if (taskbar == null)
                {
                    taskbar = (ITaskbarList3)new TaskbarListRcw();
                    taskbar.HrInit();
                }
                if (state != lastState) taskbar.SetProgressState(window, state);
                if (state != TbpfNoProgress) taskbar.SetProgressValue(window, value, 1000);
                lastState = state;
                lastValue = value;
            }
            catch (COMException) { unavailable = true; }
            catch (InvalidCastException) { unavailable = true; }
        }

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        private class TaskbarListRcw { }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, int flags);
        }
    }
}
