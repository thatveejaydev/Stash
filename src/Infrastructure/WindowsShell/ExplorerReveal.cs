using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Stash
{
    // Shows a file selected in File Explorer. On Windows 11 with Explorer already open, the folder opens
    // in a new tab of the most recent Explorer window (or the current tab if it already shows that folder).
    // Otherwise, or if anything goes wrong, Explorer opens a new window with the file selected.
    // Must be called on an STA (UI) thread.
    internal static class ExplorerReveal
    {
        private const int WmCommand = 0x0111;
        // Explorer's undocumented "new tab" command (the same one Ctrl+T sends).
        private const int NewTabCommand = 0xA21B;
        // SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED
        private const int SelectFlags = 0x1 | 0x4 | 0x8 | 0x10;
        private const int SwRestore = 9;

        private static readonly Guid SidSTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
        }

        // Only IOleWindow.GetWindow, the first method of IShellBrowser, is used.
        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig]
            int GetWindow(out IntPtr window);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public static async Task ShowFileAsync(string filePath)
        {
            try
            {
                if (await TryShowInTabAsync(filePath)) return;
            }
            catch (Exception ex) { AppLog.Error("Could not open the file in an Explorer tab", ex); }
            Process.Start("explorer.exe", "/select," + CommandLine.Quote(filePath));
        }

        private sealed class Tab
        {
            public object Browser;
            public IntPtr Window;
        }

        private static async Task<bool> TryShowInTabAsync(string filePath)
        {
            IntPtr frame = FindRecentExplorerWindow();
            if (frame == IntPtr.Zero) return false;
            // The first tab child in z-order is the one on screen. Windows 10 has no tabs.
            IntPtr activeTab = FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null);
            if (activeTab == IntPtr.Zero) return false;

            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return false;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                string folder = Path.GetDirectoryName(filePath);
                string fileName = Path.GetFileName(filePath);
                List<Tab> before = GetTabs(shell, frame);

                Tab current = before.Find(tab => tab.Window == activeTab);
                if (current != null && SameFolder(GetFolderPath(current.Browser), folder))
                {
                    Activate(frame);
                    return SelectFile(current.Browser, fileName);
                }

                SendMessage(activeTab, WmCommand, (IntPtr)NewTabCommand, (IntPtr)1);
                Tab opened = null;
                for (int attempt = 0; attempt < 40 && opened == null; attempt++)
                {
                    await Task.Delay(50);
                    foreach (Tab tab in GetTabs(shell, frame))
                        if (!before.Exists(old => old.Window == tab.Window)) { opened = tab; break; }
                }
                if (opened == null) return false;

                Invoke(opened.Browser, "Navigate2", folder);
                Activate(frame);
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    await Task.Delay(50);
                    if (SameFolder(GetFolderPath(opened.Browser), folder) && SelectFile(opened.Browser, fileName)) return true;
                }
                // The folder opened but the file could not be selected; the tab is still useful.
                return SameFolder(GetFolderPath(opened.Browser), folder);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        // FindWindowEx walks top-level windows in z-order, so the first match is the most recently used.
        private static IntPtr FindRecentExplorerWindow()
        {
            IntPtr window = IntPtr.Zero;
            while ((window = FindWindowEx(IntPtr.Zero, window, "CabinetWClass", null)) != IntPtr.Zero)
                if (IsWindowVisible(window)) return window;
            return IntPtr.Zero;
        }

        private static List<Tab> GetTabs(object shell, IntPtr frame)
        {
            List<Tab> tabs = new List<Tab>();
            object windows = Invoke(shell, "Windows");
            int count = Convert.ToInt32(Get(windows, "Count"));
            for (int i = 0; i < count; i++)
            {
                object browser;
                try { browser = Invoke(windows, "Item", i); }
                catch (TargetInvocationException) { continue; }
                if (browser == null) continue;
                try
                {
                    if (new IntPtr(Convert.ToInt64(Get(browser, "HWND"))) != frame) continue;
                    IntPtr tabWindow = GetTabWindow(browser);
                    if (tabWindow != IntPtr.Zero) tabs.Add(new Tab { Browser = browser, Window = tabWindow });
                }
                catch (TargetInvocationException) { }
                catch (COMException) { }
            }
            return tabs;
        }

        private static IntPtr GetTabWindow(object browser)
        {
            IServiceProvider provider = browser as IServiceProvider;
            if (provider == null) return IntPtr.Zero;
            Guid service = SidSTopLevelBrowser, iid = IidShellBrowser;
            object result;
            if (provider.QueryService(ref service, ref iid, out result) != 0 || result == null) return IntPtr.Zero;
            IShellBrowser shellBrowser = result as IShellBrowser;
            IntPtr window;
            return shellBrowser != null && shellBrowser.GetWindow(out window) == 0 ? window : IntPtr.Zero;
        }

        private static string GetFolderPath(object browser)
        {
            try
            {
                object document = Get(browser, "Document");
                if (document == null) return null;
                object folder = Get(document, "Folder");
                object self = folder == null ? null : Get(folder, "Self");
                return self == null ? null : Get(self, "Path") as string;
            }
            catch (TargetInvocationException) { return null; }
            catch (COMException) { return null; }
        }

        private static bool SelectFile(object browser, string fileName)
        {
            try
            {
                object document = Get(browser, "Document");
                object folder = Get(document, "Folder");
                object item = Invoke(folder, "ParseName", fileName);
                if (item == null) return false;
                Invoke(document, "SelectItem", item, SelectFlags);
                return true;
            }
            catch (TargetInvocationException) { return false; }
            catch (COMException) { return false; }
        }

        private static bool SameFolder(string a, string b)
        {
            return StorageFileService.SameDirectory(a, b);
        }

        private static void Activate(IntPtr frame)
        {
            if (IsIconic(frame)) ShowWindow(frame, SwRestore);
            SetForegroundWindow(frame);
        }

        private static object Get(object target, string property)
        {
            return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
        }

        private static object Invoke(object target, string method, params object[] args)
        {
            return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
        }
    }
}
