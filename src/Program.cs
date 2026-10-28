using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
{
    public enum DownloadMode
    {
        BestVideoAndAudioMp4,
        BestVideoAndAudioMkv,
        BestAudioOnlyMp3,
        BestAudioOnlyM4a,
        BestAudioOnlyFlac
    }

    public enum VideoResolution
    {
        Best,
        Uhd2160p,
        Qhd1440p,
        Fhd1080p,
        Hd720p,
        Sd480p,
        Low360p
    }

    public enum QueueItemStatus
    {
        Queued,
        FetchingInfo,
        Downloading,
        Merging,
        Completed,
        Cancelled,
        Error
    }

    public class DownloadQueueItem
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DownloadMode Mode { get; set; }
        public VideoResolution Resolution { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EmbedThumbnail { get; set; }
        public int ConcurrentFragments { get; set; }
        public string BadgeLabel { get; set; }
        public string OutputFolder { get; set; }
        public string CustomBaseFileName { get; set; }
        public QueueItemStatus Status { get; set; }
        public string StatusText { get; set; }
        public double ProgressPercent { get; set; }
        public string Speed { get; set; }
        public string Eta { get; set; }
        public string OutputFilePath { get; set; }
        public Process ActiveProcess { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }

        public DownloadQueueItem(
            string url,
            DownloadMode mode,
            VideoResolution resolution,
            bool embedMetadata,
            bool embedThumbnail,
            int concurrentFragments,
            string badgeLabel,
            string outputFolder)
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Url = url;
            Title = url;
            Mode = mode;
            Resolution = resolution;
            EmbedMetadata = embedMetadata;
            EmbedThumbnail = embedThumbnail;
            ConcurrentFragments = Math.Max(1, Math.Min(16, concurrentFragments));
            BadgeLabel = badgeLabel;
            OutputFolder = outputFolder;
            CustomBaseFileName = null;
            Status = QueueItemStatus.Queued;
            StatusText = "Queued";
            ProgressPercent = 0.0;
            Speed = "—";
            Eta = "—";
            CancellationSource = new CancellationTokenSource();
        }
    }

    // Persistent Settings (Remembers Save-To Directory History & User Preferences)
    public class AppSettings
    {
        public string CurrentOutputFolder { get; set; }
        public List<string> RecentFolders { get; set; }
        public int FormatIndex { get; set; }
        public int QualityIndex { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EmbedThumbnail { get; set; }
        public int ParallelCount { get; set; }
        public int ConcurrentFragments { get; set; }

        private static string GetSettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.cfg");
        }

        public AppSettings()
        {
            string defaultDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(defaultDownloads))
                defaultDownloads = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            CurrentOutputFolder = defaultDownloads;
            RecentFolders = new List<string> { defaultDownloads };
            FormatIndex = 0;
            QualityIndex = 0;
            EmbedMetadata = true;
            EmbedThumbnail = true;
            ParallelCount = 2;
            ConcurrentFragments = 8; // Default 8x Multi-Connection Turbo
        }

        public void AddFolderToHistory(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = folder.Trim();
            CurrentOutputFolder = folder;

            for (int i = RecentFolders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(RecentFolders[i], folder, StringComparison.OrdinalIgnoreCase))
                {
                    RecentFolders.RemoveAt(i);
                }
            }

            RecentFolders.Insert(0, folder);
            while (RecentFolders.Count > 8)
            {
                RecentFolders.RemoveAt(RecentFolders.Count - 1);
            }
            Save();
        }

        public void RemoveFolderFromHistory(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;

            for (int i = RecentFolders.Count - 1; i >= 0; i--)
            {
                if (string.Equals(RecentFolders[i], folder, StringComparison.OrdinalIgnoreCase))
                {
                    RecentFolders.RemoveAt(i);
                }
            }

            if (RecentFolders.Count == 0)
            {
                string defaultDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(defaultDownloads))
                    defaultDownloads = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                RecentFolders.Add(defaultDownloads);
            }

            if (string.Equals(CurrentOutputFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                CurrentOutputFolder = RecentFolders[0];
            }

            Save();
        }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return s;

                List<string> loadedHistory = new List<string>();
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    int parsedInt;
                    bool parsedBool;
                    if (key == "CurrentOutputFolder" && !string.IsNullOrEmpty(val))
                        s.CurrentOutputFolder = val;
                    else if (key == "RecentFolder" && !string.IsNullOrEmpty(val))
                        loadedHistory.Add(val);
                    else if (key == "FormatIndex" && int.TryParse(val, out parsedInt))
                        s.FormatIndex = Math.Max(0, Math.Min(4, parsedInt));
                    else if (key == "QualityIndex" && int.TryParse(val, out parsedInt))
                        s.QualityIndex = Math.Max(0, Math.Min(6, parsedInt));
                    else if (key == "EmbedMetadata" && bool.TryParse(val, out parsedBool))
                        s.EmbedMetadata = parsedBool;
                    else if (key == "EmbedThumbnail" && bool.TryParse(val, out parsedBool))
                        s.EmbedThumbnail = parsedBool;
                    else if (key == "ParallelCount" && int.TryParse(val, out parsedInt))
                        s.ParallelCount = Math.Max(1, Math.Min(6, parsedInt));
                    else if (key == "ConcurrentFragments" && int.TryParse(val, out parsedInt))
                        s.ConcurrentFragments = Math.Max(1, Math.Min(16, parsedInt));
                }

                if (loadedHistory.Count > 0)
                {
                    s.RecentFolders = loadedHistory;
                }
                s.AddFolderToHistory(s.CurrentOutputFolder);
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CurrentOutputFolder=" + CurrentOutputFolder);
                sb.AppendLine("FormatIndex=" + FormatIndex);
                sb.AppendLine("QualityIndex=" + QualityIndex);
                sb.AppendLine("EmbedMetadata=" + EmbedMetadata);
                sb.AppendLine("EmbedThumbnail=" + EmbedThumbnail);
                sb.AppendLine("ParallelCount=" + ParallelCount);
                sb.AppendLine("ConcurrentFragments=" + ConcurrentFragments);
                foreach (string dir in RecentFolders)
                {
                    sb.AppendLine("RecentFolder=" + dir);
                }
                File.WriteAllText(GetSettingsPath(), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // Native Windows Vista/10/11 File Explorer Folder Picker (IFileOpenDialog with FOS_PICKFOLDERS)
    public static class ModernFolderPicker
    {
        public static string ShowDialog(IntPtr ownerHandle, string initialDirectory, string title)
        {
            try
            {
                IFileDialog dialog = (IFileDialog)new FileOpenDialogRCW();
                uint options;
                dialog.GetOptions(out options);
                options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOREADONLYRETURN | FOS_PATHMUSTEXIST;
                dialog.SetOptions(options);

                if (!string.IsNullOrEmpty(title))
                {
                    dialog.SetTitle(title);
                }

                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    IShellItem folderItem;
                    Guid shellItemGuid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref shellItemGuid, out folderItem) == 0 && folderItem != null)
                    {
                        dialog.SetFolder(folderItem);
                    }
                }

                int hr = dialog.Show(ownerHandle);
                if (hr == 0)
                {
                    IShellItem resultItem;
                    dialog.GetResult(out resultItem);
                    if (resultItem != null)
                    {
                        string selectedPath;
                        resultItem.GetDisplayName(SIGDN_FILESYSPATH, out selectedPath);
                        return selectedPath;
                    }
                }
            }
            catch
            {
                // Fallback only if COM call ever fails
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = initialDirectory;
                    if (fbd.ShowDialog() == DialogResult.OK)
                        return fbd.SelectedPath;
                }
            }
            return null;
        }

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PathMustExist = 0x00000800;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint FOS_NOREADONLYRETURN = 0x00008000;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            [In] IntPtr pbc,
            [In] ref Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show([In] IntPtr parent);
            void SetFileTypes([In] uint cFileTypes, [In] IntPtr rgFilterSpec);
            void SetFileTypeIndex([In] uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise([In, MarshalAs(UnmanagedType.Interface)] IntPtr pfde, out uint pdwCookie);
            void Unadvise([In] uint dwCookie);
            void SetOptions([In] uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void GetFolder([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, int fdap);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close([MarshalAs(UnmanagedType.Error)] int hr);
            void SetClientGuid([In] ref Guid guid);
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.Interface)] IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler([In] IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void GetDisplayName([In] uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes([In] uint sfgaoMask, out uint psfgaoAttribs);
            void Compare([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] uint hint, out int piOrder);
        }
    }

    // Modern Palette Constants (Purple Primary Accent)
    internal static class Theme
    {
        public static readonly Color BgWindow = Color.FromArgb(20, 21, 26);
        public static readonly Color BgTopBar = Color.FromArgb(26, 28, 34);
        public static readonly Color BgSurface = Color.FromArgb(30, 33, 40);
        public static readonly Color BgSurfaceHover = Color.FromArgb(38, 42, 52);
        public static readonly Color BgInput = Color.FromArgb(17, 19, 24);
        public static readonly Color BorderSubtle = Color.FromArgb(46, 50, 62);
        public static readonly Color BorderHover = Color.FromArgb(75, 82, 100);
        public static readonly Color BorderFocus = Color.FromArgb(139, 92, 246);

        public static readonly Color TextPrimary = Color.FromArgb(244, 245, 248);
        public static readonly Color TextSecondary = Color.FromArgb(158, 165, 180);
        public static readonly Color TextMuted = Color.FromArgb(110, 117, 132);

        // Vibrant Purple Brand Colors
        public static readonly Color BrandPurple = Color.FromArgb(124, 58, 237);
        public static readonly Color BrandPurpleHover = Color.FromArgb(139, 92, 246);
        public static readonly Color BrandPurplePress = Color.FromArgb(109, 40, 217);
        public static readonly Color BrandPurpleLight = Color.FromArgb(196, 181, 253);

        public static readonly Color AccentSuccess = Color.FromArgb(34, 197, 94);
        public static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);
        public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);
        public static readonly Color AccentPurple = Color.FromArgb(168, 85, 247);

        public static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (radius <= 0 || rect.Width <= d || rect.Height <= d)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawChevronDown(Graphics g, Point center, Color color)
        {
            using (Pen p = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                Point[] pts = new Point[]
                {
                    new Point(center.X - 5, center.Y - 2),
                    new Point(center.X, center.Y + 3),
                    new Point(center.X + 5, center.Y - 2)
                };
                g.DrawLines(p, pts);
            }
        }
    }

    // Purple "Paste Link" Hero Button (Link-plus icon + "Paste Link")
    public class PasteLinkHeroButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public PasteLinkHeroButton()
        {
            Size = new Size(154, 40);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Bold);
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
            isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) isPressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgTopBar;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = isPressed ? Theme.BrandPurplePress : (isHovered ? Theme.BrandPurpleHover : Theme.BrandPurple);

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, 6))
            using (SolidBrush b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
            }

            int iconCenterX = 33;
            int iconCenterY = Height / 2;
            DrawClipboardPasteIcon(g, new Point(iconCenterX, iconCenterY), Color.White, fill);

            Rectangle textRect = new Rectangle(48, 0, Width - 54, Height);
            TextRenderer.DrawText(g, "Paste Link", Font, textRect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private void DrawClipboardPasteIcon(Graphics g, Point center, Color fg, Color bg)
        {
            // Crisp Clipboard + Down Arrow icon
            Rectangle boardRect = new Rectangle(center.X - 6, center.Y - 6, 12, 14);
            using (Pen p = new Pen(fg, 1.6f))
            using (GraphicsPath boardPath = Theme.CreateRoundedRectangle(boardRect, 2))
            {
                g.DrawPath(p, boardPath);
            }

            // Top clip tab
            Rectangle clipClear = new Rectangle(center.X - 4, center.Y - 8, 8, 4);
            using (SolidBrush clearBrush = new SolidBrush(bg))
            {
                g.FillRectangle(clearBrush, clipClear);
            }
            Rectangle clipRect = new Rectangle(center.X - 3, center.Y - 8, 6, 3);
            using (Pen p = new Pen(fg, 1.5f))
            using (GraphicsPath clipPath = Theme.CreateRoundedRectangle(clipRect, 1))
            {
                g.DrawPath(p, clipPath);
            }

            // Downward arrow inside the clipboard
            using (Pen arrowPen = new Pen(fg, 1.5f))
            {
                arrowPen.StartCap = LineCap.Round;
                arrowPen.EndCap = LineCap.Round;
                int ax = center.X;
                int topY = center.Y - 2;
                int botY = center.Y + 4;
                g.DrawLine(arrowPen, ax, topY, ax, botY);
                g.DrawLine(arrowPen, ax - 2, botY - 2, ax, botY);
                g.DrawLine(arrowPen, ax + 2, botY - 2, ax, botY);
            }
        }
    }

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
            Size = new Size(38, 20);
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

            Rectangle pillRect = new Rectangle(0, 0, Width - 1, Height - 1);
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

    // Custom Owner-Drawn Popup Menu Item (Supports aligned Checkmark + Main Title + Right Pill Tag or Bottom Subtext + Optional Remove Button)
    public class PopupMenuRow : Control
    {
        public bool IsSelected { get; set; }
        public string CheckGlyph { get; set; }
        public string TitleText { get; set; }
        public string RightBadgeText { get; set; }
        public string SubText { get; set; }
        public Color TitleOverrideColor { get; set; }
        public bool AllowRemove { get; set; }

        private bool isHovered = false;
        private bool isRemoveHovered = false;

        public event EventHandler RemoveClicked;

        public PopupMenuRow(string title, string rightBadge, string subText, bool selected, string checkGlyph = "✓")
        {
            TitleText = title;
            RightBadgeText = rightBadge;
            SubText = subText;
            IsSelected = selected;
            CheckGlyph = checkGlyph;
            TitleOverrideColor = Color.Empty;
            AllowRemove = false;
            Height = string.IsNullOrEmpty(subText) ? 34 : 46;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        private Rectangle GetRemoveButtonRect()
        {
            return new Rectangle(Width - 32, (Height - 24) / 2, 24, 24);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (AllowRemove)
            {
                bool hitRemove = GetRemoveButtonRect().Contains(e.Location);
                if (hitRemove != isRemoveHovered)
                {
                    isRemoveHovered = hitRemove;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovered = false;
            isRemoveHovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && AllowRemove && GetRemoveButtonRect().Contains(e.Location))
            {
                if (RemoveClicked != null) RemoveClicked(this, EventArgs.Empty);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = isHovered
                ? Color.FromArgb(44, 48, 60)
                : (IsSelected ? Color.FromArgb(36, 39, 50) : Theme.BgSurface);

            using (SolidBrush b = new SolidBrush(bg))
            {
                g.FillRectangle(b, ClientRectangle);
            }

            Color mainColor = TitleOverrideColor != Color.Empty
                ? TitleOverrideColor
                : (IsSelected ? Theme.BrandPurpleLight : Theme.TextPrimary);

            // Fixed 28px left gutter so all rows align to the exact same pixel column
            if (!string.IsNullOrEmpty(CheckGlyph) && (IsSelected || CheckGlyph == "☐" || CheckGlyph == "☑"))
            {
                using (Font fCheck = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
                {
                    Rectangle rCheck = new Rectangle(6, 0, 22, string.IsNullOrEmpty(SubText) ? Height : 24);
                    if (!string.IsNullOrEmpty(SubText)) rCheck.Y = 4;
                    TextRenderer.DrawText(g, CheckGlyph, fCheck, rCheck,
                        IsSelected ? Theme.BrandPurpleLight : Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            int contentLeft = string.IsNullOrEmpty(CheckGlyph) ? 14 : 32;
            int rightMargin = AllowRemove ? 38 : 10;

            if (string.IsNullOrEmpty(SubText))
            {
                // Single-line mode with optional right-aligned detail badge
                int rightReserve = string.IsNullOrEmpty(RightBadgeText) ? rightMargin : 120;
                Rectangle rTitle = new Rectangle(contentLeft, 0, Width - contentLeft - rightReserve, Height);
                using (Font fTitle = new Font("Segoe UI", 9.2F, IsSelected ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, TitleText, fTitle, rTitle, mainColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                if (!string.IsNullOrEmpty(RightBadgeText))
                {
                    Rectangle rRight = new Rectangle(Width - 125, 0, 115, Height);
                    using (Font fSub = new Font("Segoe UI", 8.2F, FontStyle.Regular))
                    {
                        TextRenderer.DrawText(g, RightBadgeText, fSub, rRight, Theme.TextMuted,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
            else
            {
                // Two-line mode: Title on top, smaller muted subtext directly underneath
                Rectangle rTitle = new Rectangle(contentLeft, 4, Width - contentLeft - rightMargin, 20);
                using (Font fTitle = new Font("Segoe UI", 9.2F, (IsSelected || AllowRemove) ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, TitleText, fTitle, rTitle, mainColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }

                Rectangle rSub = new Rectangle(contentLeft, 23, Width - contentLeft - rightMargin, 17);
                using (Font fTiny = new Font("Segoe UI", 7.3F, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, SubText, fTiny, rSub, Theme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            // Draw right-side Remove ('✕') button when hovering a removable folder row
            if (AllowRemove && (isHovered || isRemoveHovered))
            {
                Rectangle remRect = GetRemoveButtonRect();
                if (isRemoveHovered)
                {
                    using (GraphicsPath rp = Theme.CreateRoundedRectangle(remRect, 5))
                    using (SolidBrush rb = new SolidBrush(Color.FromArgb(62, 28, 35)))
                    {
                        g.FillPath(rb, rp);
                    }
                }

                using (Font fRem = new Font("Segoe UI", 8.5F, FontStyle.Bold))
                {
                    Color xCol = isRemoveHovered ? Theme.AccentDanger : Theme.TextSecondary;
                    TextRenderer.DrawText(g, "✕", fRem, remRect, xCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }

    // Inline Frameless Flow Selector ("Download  Video ⌄", "Quality  Best ⌄", etc.)
    public class InlineFlowSelector : Control
    {
        public string PrefixLabel { get; set; }
        public string ValueLabel { get; set; }
        private bool isHovered = false;
        private bool isOpen = false;

        public event EventHandler Clicked;

        public InlineFlowSelector(string prefix, string initialValue)
        {
            PrefixLabel = prefix;
            ValueLabel = initialValue;
            Height = 36;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            RecalculateWidth();
        }

        public void SetValueLabel(string val)
        {
            ValueLabel = val;
            RecalculateWidth();
            Invalidate();
        }

        public void SetOpenState(bool open)
        {
            isOpen = open;
            Invalidate();
        }

        public void RecalculateWidth()
        {
            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                int w1 = string.IsNullOrEmpty(PrefixLabel) ? 0 : TextRenderer.MeasureText(PrefixLabel, fPrefix).Width;
                int w2 = TextRenderer.MeasureText(ValueLabel, fValue).Width;
                Width = w1 + w2 + 30;
            }
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
            if (Enabled && e.Button == MouseButtons.Left)
            {
                if (Clicked != null) Clicked(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgTopBar;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            if ((isHovered || isOpen) && Enabled)
            {
                Rectangle pill = new Rectangle(0, 2, Width - 1, Height - 4);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(pill, 6))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(38, 42, 52)))
                {
                    g.FillPath(hb, path);
                }
            }

            using (Font fPrefix = new Font("Segoe UI", 10F, FontStyle.Regular))
            using (Font fValue = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            {
                int x = 6;
                if (!string.IsNullOrEmpty(PrefixLabel))
                {
                    Size sPref = TextRenderer.MeasureText(g, PrefixLabel, fPrefix, new Size(500, Height), TextFormatFlags.NoPadding);
                    Rectangle rPref = new Rectangle(x, 0, sPref.Width, Height);
                    TextRenderer.DrawText(g, PrefixLabel, fPrefix, rPref,
                        Enabled ? Theme.TextSecondary : Theme.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += sPref.Width + 6;
                }

                Size sVal = TextRenderer.MeasureText(g, ValueLabel, fValue, new Size(500, Height), TextFormatFlags.NoPadding);
                Rectangle rVal = new Rectangle(x, 0, sVal.Width, Height);
                TextRenderer.DrawText(g, ValueLabel, fValue, rVal,
                    Enabled ? Theme.TextPrimary : Theme.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += sVal.Width + 11;

                Theme.DrawChevronDown(g, new Point(x, Height / 2 + 1),
                    Enabled ? Theme.TextSecondary : Theme.TextMuted);
            }
        }
    }

    // Custom Rounded Modern Button
    public class ModernButton : Button
    {
        public Color BaseColor { get; set; }
        public Color HoverColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }
        private bool isHovered = false;
        private bool isPressed = false;

        public ModernButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            BaseColor = Theme.BgSurface;
            HoverColor = Theme.BgSurfaceHover;
            BorderColor = Color.Empty;
            CornerRadius = 6;
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
            isPressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            if (mevent.Button == MouseButtons.Left) isPressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgWindow;
            using (SolidBrush clearBrush = new SolidBrush(parentColor))
            {
                g.FillRectangle(clearBrush, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled
                ? Color.FromArgb(30, 34, 45)
                : (isPressed ? ControlPaint.Dark(BaseColor, 0.05f) : (isHovered ? HoverColor : BaseColor));

            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush b = new SolidBrush(fill))
                {
                    g.FillPath(b, path);
                }

                if (BorderColor != Color.Empty)
                {
                    using (Pen p = new Pen(isHovered ? Theme.BorderHover : BorderColor, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
            }

            Color textCol = Enabled ? ForeColor : Theme.TextMuted;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textCol,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Custom Rounded Card Panel
    public class CardPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color CardFillColor { get; set; }
        public Color CardBorderColor { get; set; }

        public CardPanel()
        {
            CornerRadius = 8;
            CardFillColor = Theme.BgSurface;
            CardBorderColor = Theme.BorderSubtle;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentColor = Parent != null ? Parent.BackColor : Theme.BgWindow;
            using (SolidBrush clear = new SolidBrush(parentColor))
            {
                g.FillRectangle(clear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush b = new SolidBrush(CardFillColor))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(CardBorderColor, 1f))
                {
                    g.DrawPath(p, path);
                }
            }
        }
    }

    // Modern Dark Dialog to Prompt for Renaming When a Similar Filename Exists
    public class RenamePromptDialog : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private TextBox txtNewName;
        public string ChosenBaseName { get; private set; }

        public RenamePromptDialog(string existingFileName, string suggestedNewBaseName, string extension)
        {
            Text = "Similar File Already Exists";
            Size = new Size(520, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.BgWindow;
            ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            Padding = new Padding(18);

            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                }
            }
            catch { }

            Label lblTitle = new Label
            {
                Text = "A file with a similar name already exists in this folder:",
                Location = new Point(18, 16),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary
            };

            Label lblExisting = new Label
            {
                Text = existingFileName,
                Location = new Point(18, 40),
                Size = new Size(468, 22),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Theme.AccentWarning
            };

            Label lblPrompt = new Label
            {
                Text = string.Format("Enter filename for this download ({0}):", extension),
                Location = new Point(18, 72),
                AutoSize = true,
                ForeColor = Theme.TextSecondary
            };

            Panel pnlInput = new Panel
            {
                Location = new Point(18, 98),
                Size = new Size(468, 34),
                Padding = new Padding(8, 6, 8, 4),
                BackColor = Theme.BgInput
            };
            pnlInput.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderFocus, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnlInput.Width - 1, pnlInput.Height - 1);
                }
            };

            txtNewName = new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.BgInput,
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI", 10F),
                Text = suggestedNewBaseName
            };
            pnlInput.Controls.Add(txtNewName);

            ModernButton btnCancel = new ModernButton
            {
                Text = "Cancel Download",
                Location = new Point(226, 150),
                Size = new Size(125, 34),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 9F)
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            ModernButton btnSave = new ModernButton
            {
                Text = "Save & Download",
                Location = new Point(359, 150),
                Size = new Size(127, 34),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
            btnSave.Click += (s, e) =>
            {
                string cleaned = SanitizeFileName(txtNewName.Text.Trim());
                if (string.IsNullOrEmpty(cleaned))
                {
                    txtNewName.Focus();
                    return;
                }
                ChosenBaseName = cleaned;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(lblTitle);
            Controls.Add(lblExisting);
            Controls.Add(lblPrompt);
            Controls.Add(pnlInput);
            Controls.Add(btnCancel);
            Controls.Add(btnSave);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Shown += (s, e) =>
            {
                txtNewName.Focus();
                txtNewName.SelectAll();
            };
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            StringBuilder sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString().Trim().TrimEnd('.');
        }
    }

    // Virtual Smooth Queue Canvas for modern card-like rows
    public class QueueListControl : Control
    {
        private readonly List<DownloadQueueItem> items = new List<DownloadQueueItem>();
        public DownloadQueueItem SelectedItem { get; private set; }
        private DownloadQueueItem hoveredItem = null;
        private VScrollBar scrollBar;
        private const int RowHeight = 64;
        private const int RowGap = 6;

        public event EventHandler SelectionChanged;
        public event Action<DownloadQueueItem> CancelRequested;
        public event Action<DownloadQueueItem> OpenFileFolderRequested;

        public QueueListControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.BgWindow;

            scrollBar = new VScrollBar
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
            int totalHeight = items.Count * (RowHeight + RowGap) + 8;
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

                Rectangle actionRect = GetActionButtonRect(rowRect);
                if (actionRect.Contains(e.Location))
                {
                    if (IsActiveStatus(hit.Status))
                    {
                        if (CancelRequested != null) CancelRequested(hit);
                    }
                    else if (hit.Status == QueueItemStatus.Completed)
                    {
                        if (OpenFileFolderRequested != null) OpenFileFolderRequested(hit);
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

        private DownloadQueueItem HitTestItem(Point pt)
        {
            int idx;
            Rectangle r;
            return HitTestItemDetailed(pt, out idx, out r);
        }

        private DownloadQueueItem HitTestItemDetailed(Point pt, out int hitIndex, out Rectangle hitRect)
        {
            hitIndex = -1;
            hitRect = Rectangle.Empty;
            int yOffset = -scrollBar.Value + 4;
            int rightPad = scrollBar.Visible ? scrollBar.Width + 6 : 2;
            int width = ClientSize.Width - rightPad - 2;

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = new Rectangle(2, yOffset + i * (RowHeight + RowGap), width, RowHeight);
                if (r.Contains(pt))
                {
                    hitIndex = i;
                    hitRect = r;
                    return items[i];
                }
            }
            return null;
        }

        private Rectangle GetActionButtonRect(Rectangle rowRect)
        {
            return new Rectangle(rowRect.Right - 38, rowRect.Y + (rowRect.Height - 26) / 2, 26, 26);
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

            int yOffset = -scrollBar.Value + 4;
            int rightPad = scrollBar.Visible ? scrollBar.Width + 6 : 2;
            int width = ClientSize.Width - rightPad - 2;

            for (int i = 0; i < items.Count; i++)
            {
                Rectangle rowRect = new Rectangle(2, yOffset + i * (RowHeight + RowGap), width, RowHeight);
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
                Rectangle r1 = new Rectangle(0, ClientSize.Height / 2 - 36, ClientSize.Width, 26);
                Rectangle r2 = new Rectangle(0, ClientSize.Height / 2 - 8, ClientSize.Width, 24);
                Rectangle r3 = new Rectangle(0, ClientSize.Height / 2 + 18, ClientSize.Width, 22);
                TextRenderer.DrawText(g, title, fTitle, r1, Theme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, sub, fSub, r2, Theme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, credit, fCredit, r3, Color.FromArgb(92, 98, 114), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawQueueCard(Graphics g, DownloadQueueItem item, Rectangle rect)
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
            else if (item.Status == QueueItemStatus.Error || item.Status == QueueItemStatus.Cancelled) statusColor = Theme.AccentDanger;
            else if (item.Status == QueueItemStatus.Merging) statusColor = Theme.AccentPurple;
            else if (item.Status == QueueItemStatus.Queued) statusColor = Theme.TextMuted;

            // Left Accent Pill Indicator
            Rectangle indicatorRect = new Rectangle(rect.X + 10, rect.Y + 12, 4, rect.Height - 24);
            using (GraphicsPath indPath = Theme.CreateRoundedRectangle(indicatorRect, 2))
            using (SolidBrush indBrush = new SolidBrush(statusColor))
            {
                g.FillPath(indBrush, indPath);
            }

            // Format & Resolution Badge
            string badgeText = item.BadgeLabel;
            using (Font fBadge = new Font("Segoe UI Semibold", 7.8F, FontStyle.Bold))
            {
                Size badgeSize = TextRenderer.MeasureText(badgeText, fBadge);
                Rectangle badgeRect = new Rectangle(rect.X + 24, rect.Y + 11, badgeSize.Width + 10, 19);
                using (GraphicsPath bp = Theme.CreateRoundedRectangle(badgeRect, 4))
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(42, 38, 62)))
                {
                    g.FillPath(bb, bp);
                }
                TextRenderer.DrawText(g, badgeText, fBadge, badgeRect, Theme.BrandPurpleLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Title
                int titleLeft = badgeRect.Right + 10;
                int titleWidth = Math.Max(80, rect.Width - titleLeft - 230);
                Rectangle titleRect = new Rectangle(titleLeft, rect.Y + 9, titleWidth, 22);
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
            else if (item.Status == QueueItemStatus.Completed)
            {
                telemetryText = "Completed  •  100%";
            }

            Rectangle statusRect = new Rectangle(rect.Right - 255, rect.Y + 10, 208, 20);
            using (Font fStatus = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, telemetryText, fStatus, statusRect, statusColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            // Bottom Sleek Progress Bar Track
            Rectangle trackRect = new Rectangle(rect.X + 24, rect.Bottom - 16, rect.Width - 72, 6);
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

            // Quick Action Button on Right (Cancel '✕' for active, Open Folder '↗' for completed)
            Rectangle actRect = GetActionButtonRect(rect);
            if (IsActiveStatus(item.Status))
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

    public class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private AppSettings settings;

        // Top Flow Controls
        private PasteLinkHeroButton btnPasteHero;
        private CompactToggleSwitch toggleSmartMode;
        private InlineFlowSelector selDownloadFormat;
        private InlineFlowSelector selQuality;
        private InlineFlowSelector selSaveTo;
        private InlineFlowSelector selOptions;

        // Queue & Status Bar Controls
        private ModernButton btnCancelSelected;
        private ModernButton btnRemoveFinished;
        private ModernButton btnInstallTools;
        private ModernButton btnToggleLog;
        private Label lblToolsStatus;
        private Label lblQueueStats;
        private QueueListControl queueListView;
        private RichTextBox txtLog;
        private Panel pnlBottomLog;
        private ToolTip appToolTip;

        private readonly List<DownloadQueueItem> queue = new List<DownloadQueueItem>();
        private readonly object queueLock = new object();
        private int activeDownloads = 0;

        private string binFolder;
        private string ytDlpPath;
        private string ffmpegPath;

        private static readonly string[] FormatShortNames = new string[]
        {
            "Video",
            "Video (MKV)",
            "Audio (MP3)",
            "Audio (M4A)",
            "Audio (FLAC)"
        };

        private static readonly string[] FormatPopupTitles = new string[]
        {
            "Video (MP4)",
            "Video (MKV)",
            "Audio (MP3)",
            "Audio (M4A)",
            "Audio (FLAC)"
        };

        private static readonly string[] FormatPopupRightTags = new string[]
        {
            "Best Compatibility",
            "4K / 8K Lossless",
            "320 kbps",
            "Original AAC",
            "Lossless Audio"
        };

        private static readonly string[] QualityShortNames = new string[]
        {
            "Best",
            "4K (2160p)",
            "2K (1440p)",
            "1080p",
            "720p",
            "480p",
            "360p"
        };

        private static readonly string[] QualityPopupTitles = new string[]
        {
            "Best",
            "4K UHD",
            "2K QHD",
            "Full HD",
            "HD",
            "SD",
            "Data Saver"
        };

        private static readonly string[] QualityPopupRightTags = new string[]
        {
            "Max Source Quality",
            "2160p",
            "1440p",
            "1080p",
            "720p",
            "480p",
            "360p"
        };

        private static readonly Regex ProgressRegex = new Regex(
            @"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%\s+of\s+~?\s*(?<size>\S+)(?:\s+at\s+(?<speed>\S+))?(?:\s+ETA\s+(?<eta>\S+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DestinationRegex = new Regex(
            @"\[(?:download|Merger|ExtractAudio)\]\s+(?:Destination:\s+|Merging formats into\s+""?)(?<path>[^""\r\n]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            KeyPreview = true;
            settings = AppSettings.Load();
            InitializePaths();
            InitializeUi();
            ApplyDarkTitleBar();
            CheckToolsStatus();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                }
            }
            catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                e.SuppressKeyPress = true;
                PasteFromClipboardAndDownload();
            }
        }

        private void InitializePaths()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            binFolder = Path.Combine(appDir, "bin");
            if (!Directory.Exists(binFolder))
            {
                Directory.CreateDirectory(binFolder);
            }

            ytDlpPath = FindExecutable("yt-dlp.exe");
            ffmpegPath = FindExecutable("ffmpeg.exe");
        }

        private string FindExecutable(string exeName)
        {
            string localBin = Path.Combine(binFolder, exeName);
            if (File.Exists(localBin)) return localBin;

            string localRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
            if (File.Exists(localRoot)) return localRoot;

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        string candidate = Path.Combine(dir.Trim(), exeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch { }
            }

            return localBin;
        }

        private string GetFolderShortDisplayName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return "Downloads";
            string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(leaf)) return trimmed;
            if (leaf.Length > 20) return leaf.Substring(0, 18) + "…";
            return leaf;
        }

        private string GetEmbedShortDisplayName()
        {
            if (settings.EmbedMetadata && settings.EmbedThumbnail) return "Tags + Art";
            if (settings.EmbedMetadata) return "Metadata";
            if (settings.EmbedThumbnail) return "Thumbnail";
            return "None";
        }

        private string GetOptionsSummaryLabel()
        {
            return string.Format("{0} • {1} • {2}x",
                GetEmbedShortDisplayName(),
                settings.ParallelCount,
                settings.ConcurrentFragments);
        }

        private void InitializeUi()
        {
            Text = "Stash";
            Size = new Size(1020, 660);
            MinimumSize = new Size(880, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            BackColor = Theme.BgWindow;
            ForeColor = Theme.TextPrimary;
            ShowIcon = true;
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            appToolTip = new ToolTip();

            // 1. Top Command Flow Bar
            Panel pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(18, 13, 18, 13),
                BackColor = Theme.BgTopBar
            };
            pnlTopBar.Paint += (s, e) =>
            {
                using (Pen borderPen = new Pen(Theme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(borderPen, 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
                }
            };

            FlowLayoutPanel flowTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoSize = false,
                BackColor = Theme.BgTopBar
            };

            // Purple "[🔗+ Paste Link]" Hero Button
            btnPasteHero = new PasteLinkHeroButton
            {
                Margin = new Padding(0, 0, 16, 0)
            };
            appToolTip.SetToolTip(btnPasteHero, "Paste link from clipboard & download immediately (Ctrl+V)");
            btnPasteHero.Click += (s, e) => PasteFromClipboardAndDownload();

            // Smaller Minimalist Toggle Switch (No icon)
            toggleSmartMode = new CompactToggleSwitch
            {
                Checked = true,
                Margin = new Padding(0, 10, 10, 0)
            };
            appToolTip.SetToolTip(toggleSmartMode, "Toggle quick format & quality selectors");

            // "Download  Video ⌄"
            selDownloadFormat = new InlineFlowSelector("Download", FormatShortNames[settings.FormatIndex])
            {
                Margin = new Padding(0, 2, 12, 0)
            };
            selDownloadFormat.Clicked += (s, e) => ShowFormatPopup();

            // "Quality  Best ⌄"
            selQuality = new InlineFlowSelector("Quality", QualityShortNames[settings.QualityIndex])
            {
                Margin = new Padding(0, 2, 12, 0)
            };
            selQuality.Clicked += (s, e) => ShowQualityPopup();

            // "Save to  Downloads ⌄"
            selSaveTo = new InlineFlowSelector("Save to", GetFolderShortDisplayName(settings.CurrentOutputFolder))
            {
                Margin = new Padding(0, 2, 12, 0)
            };
            appToolTip.SetToolTip(selSaveTo, settings.CurrentOutputFolder);
            selSaveTo.Clicked += (s, e) => ShowSaveToHistoryPopup();

            // Combined Nested Dropdown for "Embed", "Parallel", and "Turbo"
            selOptions = new InlineFlowSelector("Options", GetOptionsSummaryLabel())
            {
                Margin = new Padding(0, 2, 0, 0)
            };
            appToolTip.SetToolTip(selOptions, "Configure Embed Tags, Parallel Downloads, and Multi-Connection Turbo");
            selOptions.Clicked += (s, e) => ShowOptionsNestedPopup();

            toggleSmartMode.CheckedChanged += (s, e) =>
            {
                bool on = toggleSmartMode.Checked;
                selDownloadFormat.Enabled = on;
                UpdateQualitySelectorEnabledState();
                selSaveTo.Enabled = on;
                selOptions.Enabled = on;
            };

            UpdateQualitySelectorEnabledState();

            flowTop.Controls.Add(btnPasteHero);
            flowTop.Controls.Add(toggleSmartMode);
            flowTop.Controls.Add(selDownloadFormat);
            flowTop.Controls.Add(selQuality);
            flowTop.Controls.Add(selSaveTo);
            flowTop.Controls.Add(selOptions);

            pnlTopBar.Controls.Add(flowTop);

            // 2. Secondary Sub-Toolbar (Queue Stats + Engine Status + Action Buttons)
            TableLayoutPanel pnlQueueBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                ColumnCount = 6,
                RowCount = 1,
                Padding = new Padding(18, 8, 18, 4),
                BackColor = Theme.BgWindow
            };
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblQueueStats = new Label
            {
                Text = "QUEUE  •  0 ITEMS",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                ForeColor = Theme.TextMuted
            };

            lblToolsStatus = new Label
            {
                Text = "Checking engine...",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, 10, 0),
                ForeColor = Theme.AccentWarning
            };
            lblToolsStatus.Click += BtnInstallTools_Click;

            btnInstallTools = new ModernButton
            {
                Text = "Install yt-dlp + FFmpeg",
                Size = new Size(165, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = 6
            };
            btnInstallTools.Click += BtnInstallTools_Click;

            btnCancelSelected = new ModernButton
            {
                Text = "Cancel Selected",
                Size = new Size(115, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Color.FromArgb(46, 24, 28),
                HoverColor = Color.FromArgb(64, 30, 36),
                ForeColor = Color.FromArgb(252, 165, 165),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = 6
            };
            btnCancelSelected.Click += BtnCancelSelected_Click;

            btnRemoveFinished = new ModernButton
            {
                Text = "Clear Finished",
                Size = new Size(106, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = 6
            };
            btnRemoveFinished.Click += BtnRemoveFinished_Click;

            btnToggleLog = new ModernButton
            {
                Text = "Hide Console",
                Size = new Size(98, 27),
                Margin = new Padding(0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = 6
            };
            btnToggleLog.Click += (s, e) =>
            {
                pnlBottomLog.Visible = !pnlBottomLog.Visible;
                btnToggleLog.Text = pnlBottomLog.Visible ? "Hide Console" : "Show Console";
            };

            pnlQueueBar.Controls.Add(lblQueueStats, 0, 0);
            pnlQueueBar.Controls.Add(lblToolsStatus, 1, 0);
            pnlQueueBar.Controls.Add(btnInstallTools, 2, 0);
            pnlQueueBar.Controls.Add(btnCancelSelected, 3, 0);
            pnlQueueBar.Controls.Add(btnRemoveFinished, 4, 0);
            pnlQueueBar.Controls.Add(btnToggleLog, 5, 0);

            // 3. Collapsible Bottom Console Log Card
            pnlBottomLog = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 125,
                Padding = new Padding(18, 6, 18, 14),
                BackColor = Theme.BgWindow
            };

            CardPanel cardLog = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                CardFillColor = Color.FromArgb(14, 15, 19),
                CardBorderColor = Theme.BorderSubtle,
                CornerRadius = 8
            };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(14, 15, 19),
                ForeColor = Color.FromArgb(148, 163, 184),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.8F)
            };
            cardLog.Controls.Add(txtLog);
            pnlBottomLog.Controls.Add(cardLog);

            // 4. Center Custom Card Queue List
            Panel pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 4, 18, 6),
                BackColor = Theme.BgWindow
            };

            queueListView = new QueueListControl
            {
                Dock = DockStyle.Fill
            };
            queueListView.CancelRequested += CancelSingleItem;
            queueListView.OpenFileFolderRequested += OpenCompletedItemFolder;
            pnlCenter.Controls.Add(queueListView);

            Controls.Add(pnlCenter);
            Controls.Add(pnlBottomLog);
            Controls.Add(pnlQueueBar);
            Controls.Add(pnlTopBar);
        }

        private void UpdateQualitySelectorEnabledState()
        {
            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            bool isVideo = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            selQuality.Enabled = toggleSmartMode.Checked && isVideo;
        }

        // Dark Popup Helper for InlineFlowSelectors
        private ToolStripDropDown CreateDarkPopup(Control content, InlineFlowSelector ownerSelector)
        {
            ToolStripControlHost host = new ToolStripControlHost(content)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = content.Size
            };

            ToolStripDropDown popup = new ToolStripDropDown
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = content.Size,
                BackColor = Theme.BgSurface,
                DropShadowEnabled = true
            };
            popup.Items.Add(host);
            popup.Closed += (s, e) => ownerSelector.SetOpenState(false);
            ownerSelector.SetOpenState(true);
            popup.Show(ownerSelector, new Point(0, ownerSelector.Height + 2));
            return popup;
        }

        // Perfectly aligned Download Format dropdown with fixed left checkmark column and right-aligned detail tag
        private void ShowFormatPopup()
        {
            int width = 265;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = FormatPopupTitles.Length * 34 + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < FormatPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.FormatIndex);
                PopupMenuRow row = new PopupMenuRow(FormatPopupTitles[idx], FormatPopupRightTags[idx], null, active)
                {
                    Location = new Point(4, 4 + idx * 34),
                    Width = width - 8
                };
                row.MouseDown += (s, e) =>
                {
                    settings.FormatIndex = idx;
                    settings.Save();
                    selDownloadFormat.SetValueLabel(FormatShortNames[idx]);
                    UpdateQualitySelectorEnabledState();
                    if (popup != null) popup.Close();
                };
                container.Controls.Add(row);
            }
            popup = CreateDarkPopup(container, selDownloadFormat);
        }

        private void ShowQualityPopup()
        {
            int width = 245;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = QualityPopupTitles.Length * 34 + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < QualityPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.QualityIndex);
                PopupMenuRow row = new PopupMenuRow(QualityPopupTitles[idx], QualityPopupRightTags[idx], null, active)
                {
                    Location = new Point(4, 4 + idx * 34),
                    Width = width - 8
                };
                row.MouseDown += (s, e) =>
                {
                    settings.QualityIndex = idx;
                    settings.Save();
                    selQuality.SetValueLabel(QualityShortNames[idx]);
                    if (popup != null) popup.Close();
                };
                container.Controls.Add(row);
            }
            popup = CreateDarkPopup(container, selQuality);
        }

        // Save-to Dropdown: Folder Name on main line, full path in tiny text directly underneath, plus '✕' button to remove from list
        private void ShowSaveToHistoryPopup()
        {
            int width = 325;
            int itemCount = settings.RecentFolders.Count;
            int rowH = 46;
            int totalHeight = (itemCount * rowH) + 46;

            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = totalHeight
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                    int sepY = itemCount * rowH + 6;
                    e.Graphics.DrawLine(p, 8, sepY, container.Width - 8, sepY);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < itemCount; i++)
            {
                string folderPath = settings.RecentFolders[i];
                bool isCurrent = string.Equals(folderPath, settings.CurrentOutputFolder, StringComparison.OrdinalIgnoreCase);
                string leaf = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(leaf)) leaf = folderPath;

                PopupMenuRow row = new PopupMenuRow(leaf, null, folderPath, isCurrent)
                {
                    Location = new Point(4, 4 + i * rowH),
                    Width = width - 8,
                    AllowRemove = true
                };
                row.MouseDown += (s, e) =>
                {
                    SelectOutputFolder(folderPath);
                    if (popup != null) popup.Close();
                };
                row.RemoveClicked += (s, e) =>
                {
                    settings.RemoveFolderFromHistory(folderPath);
                    selSaveTo.SetValueLabel(GetFolderShortDisplayName(settings.CurrentOutputFolder));
                    appToolTip.SetToolTip(selSaveTo, settings.CurrentOutputFolder);
                    if (popup != null) popup.Close();
                    BeginInvoke((Action)ShowSaveToHistoryPopup);
                };
                container.Controls.Add(row);
            }

            // "Browse for folder..." row at bottom of popup
            PopupMenuRow browseRow = new PopupMenuRow("Browse for folder...", null, null, false, "")
            {
                Location = new Point(4, itemCount * rowH + 9),
                Width = width - 8,
                TitleOverrideColor = Theme.BrandPurpleLight
            };
            browseRow.MouseDown += (s, e) =>
            {
                if (popup != null) popup.Close();
                BeginInvoke((Action)PromptBrowseForFolder);
            };
            container.Controls.Add(browseRow);

            popup = CreateDarkPopup(container, selSaveTo);
        }

        private void SelectOutputFolder(string folderPath)
        {
            settings.AddFolderToHistory(folderPath);
            selSaveTo.SetValueLabel(GetFolderShortDisplayName(folderPath));
            appToolTip.SetToolTip(selSaveTo, folderPath);
        }

        // Opens the full modern Windows File Explorer dialog instead of the tiny tree window
        private void PromptBrowseForFolder()
        {
            string chosen = ModernFolderPicker.ShowDialog(
                Handle,
                settings.CurrentOutputFolder,
                "Select Destination Folder for Downloads");

            if (!string.IsNullOrWhiteSpace(chosen))
            {
                SelectOutputFolder(chosen);
            }
        }

        // Nested Dropdown containing "Embed", "Parallel", and "Turbo" submenus inside a single Region-shaped popup
        private void ShowOptionsNestedPopup()
        {
            int leftWidth = 225;
            int leftHeight = 3 * 34 + 8; // 110
            int gap = 4;
            int rightMaxWidth = 220;
            int totalWidth = leftWidth + gap + rightMaxWidth;
            int totalHeight = 34 + (6 * 46 + 8) + 8; // Enough height for 6 two-line rows in Parallel & 5 in Turbo

            Panel rootContainer = new Panel
            {
                BackColor = Theme.BgWindow,
                Width = totalWidth,
                Height = totalHeight
            };

            Panel pnlLeft = new Panel
            {
                BackColor = Theme.BgSurface,
                Location = new Point(0, 0),
                Width = leftWidth,
                Height = leftHeight
            };
            pnlLeft.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnlLeft.Width - 1, pnlLeft.Height - 1);
                }
            };

            Panel pnlRightHost = new Panel
            {
                BackColor = Theme.BgSurface,
                Location = new Point(leftWidth + gap, 0),
                Width = rightMaxWidth,
                Height = 76,
                Visible = false
            };

            ToolStripControlHost host = new ToolStripControlHost(rootContainer)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = rootContainer.Size
            };

            ToolStripDropDown popup = new ToolStripDropDown
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = rootContainer.Size,
                BackColor = Theme.BgSurface,
                DropShadowEnabled = false
            };
            popup.Items.Add(host);

            Action updatePopupRegion = () =>
            {
                Region reg = new Region(pnlLeft.Bounds);
                if (pnlRightHost.Visible)
                {
                    // Include the 4px bridge gap between left menu and right submenu so the mouse never leaves the popup region while crossing over
                    Rectangle bridge = new Rectangle(pnlLeft.Right, pnlRightHost.Top, gap, Math.Min(pnlLeft.Bottom, pnlRightHost.Bottom) - pnlRightHost.Top);
                    if (bridge.Height > 0) reg.Union(bridge);
                    reg.Union(pnlRightHost.Bounds);
                }
                popup.Region = reg;
            };

            PopupMenuRow rowEmbed = new PopupMenuRow(
                "Embed",
                GetEmbedShortDisplayName() + "   ▸",
                null,
                false,
                "")
            {
                Location = new Point(4, 4),
                Width = leftWidth - 8
            };

            PopupMenuRow rowParallel = new PopupMenuRow(
                "Parallel",
                settings.ParallelCount + (settings.ParallelCount == 1 ? " Download   ▸" : " Downloads   ▸"),
                null,
                false,
                "")
            {
                Location = new Point(4, 38),
                Width = leftWidth - 8
            };

            PopupMenuRow rowTurbo = new PopupMenuRow(
                "Turbo",
                settings.ConcurrentFragments + "x   ▸",
                null,
                false,
                "")
            {
                Location = new Point(4, 72),
                Width = leftWidth - 8
            };

            int currentSubmenuIndex = -1;

            Action<int, PopupMenuRow, Panel, int> showSubmenu = (index, ownerRow, childPanel, alignY) =>
            {
                if (currentSubmenuIndex == index && pnlRightHost.Visible) return;
                currentSubmenuIndex = index;

                rowEmbed.IsSelected = (ownerRow == rowEmbed);
                rowParallel.IsSelected = (ownerRow == rowParallel);
                rowTurbo.IsSelected = (ownerRow == rowTurbo);
                rowEmbed.Invalidate();
                rowParallel.Invalidate();
                rowTurbo.Invalidate();

                pnlRightHost.SuspendLayout();
                pnlRightHost.Controls.Clear();
                pnlRightHost.Location = new Point(leftWidth + gap, alignY);
                pnlRightHost.Size = childPanel.Size;
                childPanel.Location = Point.Empty;
                pnlRightHost.Controls.Add(childPanel);
                pnlRightHost.Visible = true;
                pnlRightHost.ResumeLayout();

                updatePopupRegion();
            };

            Action openEmbed = () =>
            {
                Panel sub = BuildEmbedSubmenuPanel(rowEmbed);
                showSubmenu(0, rowEmbed, sub, 0);
            };

            Action openParallel = () =>
            {
                Panel sub = BuildParallelSubmenuPanel(rowParallel, () => popup.Close());
                showSubmenu(1, rowParallel, sub, 34);
            };

            Action openTurbo = () =>
            {
                Panel sub = BuildTurboSubmenuPanel(rowTurbo, () => popup.Close());
                showSubmenu(2, rowTurbo, sub, 68);
            };

            rowEmbed.MouseEnter += (s, e) => openEmbed();
            rowEmbed.MouseDown += (s, e) => openEmbed();

            rowParallel.MouseEnter += (s, e) => openParallel();
            rowParallel.MouseDown += (s, e) => openParallel();

            rowTurbo.MouseEnter += (s, e) => openTurbo();
            rowTurbo.MouseDown += (s, e) => openTurbo();

            pnlLeft.Controls.Add(rowEmbed);
            pnlLeft.Controls.Add(rowParallel);
            pnlLeft.Controls.Add(rowTurbo);

            rootContainer.Controls.Add(pnlLeft);
            rootContainer.Controls.Add(pnlRightHost);

            updatePopupRegion();

            popup.Closed += (s, e) => selOptions.SetOpenState(false);
            selOptions.SetOpenState(true);
            popup.Show(selOptions, new Point(0, selOptions.Height + 2));
        }

        private Panel BuildEmbedSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = 235;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 76
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            PopupMenuRow rowMeta = new PopupMenuRow(
                "Metadata & Chapters",
                null,
                null,
                settings.EmbedMetadata,
                settings.EmbedMetadata ? "☑" : "☐")
            {
                Location = new Point(4, 4),
                Width = width - 8
            };
            rowMeta.MouseDown += (s, e) =>
            {
                settings.EmbedMetadata = !settings.EmbedMetadata;
                settings.Save();
                rowMeta.IsSelected = settings.EmbedMetadata;
                rowMeta.CheckGlyph = settings.EmbedMetadata ? "☑" : "☐";
                rowMeta.Invalidate();
                parentRow.RightBadgeText = GetEmbedShortDisplayName() + "   ▸";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            PopupMenuRow rowThumb = new PopupMenuRow(
                "Thumbnail Cover Art",
                null,
                null,
                settings.EmbedThumbnail,
                settings.EmbedThumbnail ? "☑" : "☐")
            {
                Location = new Point(4, 38),
                Width = width - 8
            };
            rowThumb.MouseDown += (s, e) =>
            {
                settings.EmbedThumbnail = !settings.EmbedThumbnail;
                settings.Save();
                rowThumb.IsSelected = settings.EmbedThumbnail;
                rowThumb.CheckGlyph = settings.EmbedThumbnail ? "☑" : "☐";
                rowThumb.Invalidate();
                parentRow.RightBadgeText = GetEmbedShortDisplayName() + "   ▸";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            container.Controls.Add(rowMeta);
            container.Controls.Add(rowThumb);
            return container;
        }

        private Panel BuildParallelSubmenuPanel(PopupMenuRow parentRow, Action closeAll)
        {
            int width = 190;
            int rowH = 46;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 6 * rowH + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            for (int n = 1; n <= 6; n++)
            {
                int count = n;
                bool active = (count == settings.ParallelCount);
                string title = count + (count == 1 ? " Download" : " Downloads");
                PopupMenuRow row = new PopupMenuRow(title, null, "Concurrent", active)
                {
                    Location = new Point(4, 4 + (n - 1) * rowH),
                    Width = width - 8
                };
                row.MouseDown += (s, e) =>
                {
                    settings.ParallelCount = count;
                    settings.Save();
                    parentRow.RightBadgeText = count + (count == 1 ? " Download   ▸" : " Downloads   ▸");
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    ProcessQueue();
                    if (closeAll != null) closeAll();
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private Panel BuildTurboSubmenuPanel(PopupMenuRow parentRow, Action closeAll)
        {
            int[] counts = new int[] { 1, 4, 8, 12, 16 };
            string[] titles = new string[]
            {
                "1x  (Standard)",
                "4x  (Fast)",
                "8x  (Turbo Default)",
                "12x  (High Speed)",
                "16x  (Maximum)"
            };
            string[] subTags = new string[]
            {
                "Single Stream",
                "4 Connections",
                "8 Connections",
                "12 Connections",
                "16 Connections"
            };

            int width = 210;
            int rowH = 46;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = counts.Length * rowH + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            for (int i = 0; i < counts.Length; i++)
            {
                int fragCount = counts[i];
                bool active = (fragCount == settings.ConcurrentFragments);
                PopupMenuRow row = new PopupMenuRow(titles[i], null, subTags[i], active)
                {
                    Location = new Point(4, 4 + i * rowH),
                    Width = width - 8
                };
                row.MouseDown += (s, e) =>
                {
                    settings.ConcurrentFragments = fragCount;
                    settings.Save();
                    parentRow.RightBadgeText = fragCount + "x   ▸";
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    if (closeAll != null) closeAll();
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private void CheckToolsStatus()
        {
            ytDlpPath = FindExecutable("yt-dlp.exe");
            ffmpegPath = FindExecutable("ffmpeg.exe");

            bool hasYtDlp = File.Exists(ytDlpPath);
            bool hasFfmpeg = File.Exists(ffmpegPath);

            if (hasYtDlp && hasFfmpeg)
            {
                lblToolsStatus.Text = "● Engine Ready";
                lblToolsStatus.ForeColor = Theme.AccentSuccess;
                btnInstallTools.Text = "Update Engine";
                btnInstallTools.Size = new Size(108, 27);
                btnInstallTools.BaseColor = Theme.BgSurface;
                btnInstallTools.HoverColor = Theme.BgSurfaceHover;
                btnInstallTools.BorderColor = Theme.BorderSubtle;
                btnInstallTools.ForeColor = Theme.TextSecondary;
            }
            else
            {
                List<string> missing = new List<string>();
                if (!hasYtDlp) missing.Add("yt-dlp");
                if (!hasFfmpeg) missing.Add("FFmpeg");
                lblToolsStatus.Text = "● Missing: " + string.Join(" & ", missing);
                lblToolsStatus.ForeColor = Theme.AccentWarning;
                btnInstallTools.Text = "Install yt-dlp + FFmpeg";
                btnInstallTools.Size = new Size(165, 27);
                btnInstallTools.BaseColor = Theme.BrandPurple;
                btnInstallTools.HoverColor = Theme.BrandPurpleHover;
                btnInstallTools.BorderColor = Color.Empty;
                btnInstallTools.ForeColor = Color.White;
            }
        }

        private async void BtnInstallTools_Click(object sender, EventArgs e)
        {
            if (!btnInstallTools.Enabled) return;
            btnInstallTools.Enabled = false;
            try
            {
                if (!Directory.Exists(binFolder)) Directory.CreateDirectory(binFolder);

                string targetYtDlp = Path.Combine(binFolder, "yt-dlp.exe");

                if (!File.Exists(ytDlpPath))
                {
                    AppendLog("Downloading latest yt-dlp.exe from GitHub releases...");
                    lblToolsStatus.Text = "● Downloading yt-dlp.exe...";
                    await DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", targetYtDlp);
                    AppendLog("yt-dlp.exe installed to " + targetYtDlp);
                }
                else
                {
                    AppendLog("Checking for yt-dlp updates (-U)...");
                    lblToolsStatus.Text = "● Updating yt-dlp...";
                    await Task.Run(() =>
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(ytDlpPath, "-U")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true
                            };
                            using (Process p = Process.Start(psi))
                            {
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit();
                                if (!string.IsNullOrWhiteSpace(output))
                                    AppendLog(output.Trim());
                            }
                        }
                        catch (Exception ex)
                        {
                            AppendLog("Update check failed: " + ex.Message);
                        }
                    });
                }

                ytDlpPath = FindExecutable("yt-dlp.exe");
                ffmpegPath = FindExecutable("ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    AppendLog("Downloading FFmpeg static build (yt-dlp/FFmpeg-Builds)...");
                    lblToolsStatus.Text = "● Downloading FFmpeg (~80 MB)...";
                    string zipPath = Path.Combine(binFolder, "ffmpeg.zip");
                    string ffmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
                    await DownloadFileAsync(ffmpegUrl, zipPath);

                    AppendLog("Extracting ffmpeg.exe and ffprobe.exe into ./bin...");
                    lblToolsStatus.Text = "● Extracting FFmpeg...";
                    await Task.Run(() =>
                    {
                        string extractScript = string.Format(
                            "Add-Type -AssemblyName System.IO.Compression.FileSystem; " +
                            "$zip = [System.IO.Compression.ZipFile]::OpenRead('{0}'); " +
                            "foreach ($entry in $zip.Entries) {{ " +
                            "  if ($entry.Name -eq 'ffmpeg.exe' -or $entry.Name -eq 'ffprobe.exe') {{ " +
                            "    $dest = Join-Path '{1}' $entry.Name; " +
                            "    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true); " +
                            "  }} " +
                            "}} " +
                            "$zip.Dispose(); Remove-Item '{0}' -Force",
                            zipPath.Replace("'", "''"), binFolder.Replace("'", "''"));

                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + extractScript + "\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                        }
                    });
                    AppendLog("FFmpeg ready at " + binFolder);
                }

                CheckToolsStatus();
            }
            catch (Exception ex)
            {
                AppendLog("Error installing tools: " + ex.Message);
                MessageBox.Show("Failed to download dependencies:\n" + ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CheckToolsStatus();
            }
            finally
            {
                btnInstallTools.Enabled = true;
            }
        }

        private Task DownloadFileAsync(string url, string destinationPath)
        {
            return Task.Run(() =>
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Stash-Windows-Downloader/2.5");
                    wc.DownloadFile(url, destinationPath);
                }
            });
        }

        private string BuildBadgeLabel(DownloadMode mode, VideoResolution res, bool meta, bool thumb, int concurrentFragments)
        {
            string formatPart;
            bool isVideo = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);

            if (isVideo)
            {
                string container = (mode == DownloadMode.BestVideoAndAudioMp4) ? "MP4" : "MKV";
                string resText = "BEST";
                switch (res)
                {
                    case VideoResolution.Uhd2160p: resText = "4K"; break;
                    case VideoResolution.Qhd1440p: resText = "1440P"; break;
                    case VideoResolution.Fhd1080p: resText = "1080P"; break;
                    case VideoResolution.Hd720p: resText = "720P"; break;
                    case VideoResolution.Sd480p: resText = "480P"; break;
                    case VideoResolution.Low360p: resText = "360P"; break;
                }
                formatPart = container + " • " + resText;
            }
            else
            {
                switch (mode)
                {
                    case DownloadMode.BestAudioOnlyMp3: formatPart = "MP3 • 320K"; break;
                    case DownloadMode.BestAudioOnlyM4a: formatPart = "M4A • BEST"; break;
                    case DownloadMode.BestAudioOnlyFlac: formatPart = "FLAC • LOSSLESS"; break;
                    default: formatPart = "AUDIO"; break;
                }
            }

            if (meta || thumb)
            {
                formatPart += " • TAGS";
            }

            if (concurrentFragments > 1)
            {
                formatPart += " • " + concurrentFragments + "X";
            }

            return formatPart;
        }

        private void PasteFromClipboardAndDownload()
        {
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show(
                    "Your clipboard is empty. Copy a video or audio URL first, then click 'Paste Link' or press Ctrl+V.",
                    "No Link in Clipboard",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string rawInput = Clipboard.GetText().Trim();
            if (string.IsNullOrEmpty(rawInput)) return;

            if (!File.Exists(ytDlpPath) || !File.Exists(ffmpegPath))
            {
                DialogResult dr = MessageBox.Show(
                    "yt-dlp.exe and/or ffmpeg.exe are not installed yet.\n\nWould you like to automatically download and install them right now?",
                    "Install Required Dependencies",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr == DialogResult.Yes)
                {
                    BtnInstallTools_Click(this, EventArgs.Empty);
                }
                return;
            }

            string outFolder = settings.CurrentOutputFolder;
            if (!Directory.Exists(outFolder))
            {
                try
                {
                    Directory.CreateDirectory(outFolder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create output folder:\n" + ex.Message, "Invalid Folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            settings.AddFolderToHistory(outFolder);

            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            VideoResolution res = (VideoResolution)settings.QualityIndex;
            bool embedMeta = settings.EmbedMetadata;
            bool embedThumb = settings.EmbedThumbnail;
            int concurrentFragments = settings.ConcurrentFragments;
            string badgeLabel = BuildBadgeLabel(mode, res, embedMeta, embedThumb, concurrentFragments);

            DownloadQueueItem item = new DownloadQueueItem(
                rawInput,
                mode,
                res,
                embedMeta,
                embedThumb,
                concurrentFragments,
                badgeLabel,
                outFolder);

            lock (queueLock)
            {
                queue.Insert(0, item);
            }

            queueListView.InsertItemAtTop(item);
            UpdateQueueHeaderStats();

            AppendLog(string.Format("[Queue] Pasted & Added: {0} [{1}] -> {2}", item.Url, item.BadgeLabel, outFolder));
            ProcessQueue();
        }

        private void UpdateQueueHeaderStats()
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)UpdateQueueHeaderStats);
                return;
            }

            int total = queue.Count;
            int running = queue.FindAll(x => x.Status == QueueItemStatus.Downloading || x.Status == QueueItemStatus.Merging || x.Status == QueueItemStatus.FetchingInfo).Count;
            int done = queue.FindAll(x => x.Status == QueueItemStatus.Completed).Count;

            if (running > 0)
                lblQueueStats.Text = string.Format("QUEUE  •  {0} ACTIVE  •  {1} OF {2} COMPLETED", running, done, total);
            else
                lblQueueStats.Text = string.Format("QUEUE  •  {0} ITEMS  ({1} COMPLETED)", total, done);
        }

        private void ProcessQueue()
        {
            int maxParallel = settings.ParallelCount;

            lock (queueLock)
            {
                while (activeDownloads < maxParallel)
                {
                    DownloadQueueItem nextItem = queue.Find(x => x.Status == QueueItemStatus.Queued);
                    if (nextItem == null) break;

                    nextItem.Status = QueueItemStatus.FetchingInfo;
                    nextItem.StatusText = "Fetching info...";
                    activeDownloads++;
                    RefreshQueueUi();

                    Task.Run(() => ExecuteDownloadAsync(nextItem));
                }
            }
        }

        private string GetTargetExtension(DownloadMode mode)
        {
            switch (mode)
            {
                case DownloadMode.BestVideoAndAudioMp4: return ".mp4";
                case DownloadMode.BestVideoAndAudioMkv: return ".mkv";
                case DownloadMode.BestAudioOnlyMp3: return ".mp3";
                case DownloadMode.BestAudioOnlyM4a: return ".m4a";
                case DownloadMode.BestAudioOnlyFlac: return ".flac";
                default: return ".mp4";
            }
        }

        private string FindSimilarExistingFile(string folder, string cleanTitle, string videoId)
        {
            if (!Directory.Exists(folder)) return null;
            try
            {
                string[] files = Directory.GetFiles(folder);
                foreach (string file in files)
                {
                    string ext = Path.GetExtension(file);
                    if (string.Equals(ext, ".part", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".ytdl", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ext, ".temp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string nameNoExt = Path.GetFileNameWithoutExtension(file);
                    string strippedExisting = Regex.Replace(nameNoExt, @"\s*\[[A-Za-z0-9_\-]{5,20}\]$", "").Trim();

                    if (!string.IsNullOrEmpty(cleanTitle))
                    {
                        if (string.Equals(nameNoExt, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(strippedExisting, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                            nameNoExt.StartsWith(cleanTitle, StringComparison.OrdinalIgnoreCase))
                        {
                            return Path.GetFileName(file);
                        }
                    }

                    if (!string.IsNullOrEmpty(videoId) &&
                        nameNoExt.IndexOf("[" + videoId + "]", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return Path.GetFileName(file);
                    }
                }
            }
            catch { }
            return null;
        }

        private string SuggestNextAvailableName(string folder, string baseTitle, string ext)
        {
            int counter = 2;
            while (counter < 999)
            {
                string candidate = string.Format("{0} ({1})", baseTitle, counter);
                string fullPath = Path.Combine(folder, candidate + ext);
                if (!File.Exists(fullPath))
                {
                    return candidate;
                }
                counter++;
            }
            return baseTitle + " (Copy)";
        }

        private bool CheckAndPromptForDuplicateRename(DownloadQueueItem item, string fetchedTitle, string fetchedId)
        {
            string cleanTitle = RenamePromptDialog.SanitizeFileName(fetchedTitle);
            if (string.IsNullOrEmpty(cleanTitle)) return true;

            string existingFile = FindSimilarExistingFile(item.OutputFolder, cleanTitle, fetchedId);
            if (existingFile == null)
            {
                item.CustomBaseFileName = cleanTitle;
                return true;
            }

            string ext = GetTargetExtension(item.Mode);
            string suggested = SuggestNextAvailableName(item.OutputFolder, cleanTitle, ext);

            bool accepted = false;
            Invoke((Action)(() =>
            {
                using (RenamePromptDialog dlg = new RenamePromptDialog(existingFile, suggested, ext))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        item.CustomBaseFileName = dlg.ChosenBaseName;
                        item.Title = dlg.ChosenBaseName;
                        accepted = true;
                    }
                }
            }));

            return accepted;
        }

        private int GetResolutionHeightCap(VideoResolution res)
        {
            switch (res)
            {
                case VideoResolution.Uhd2160p: return 2160;
                case VideoResolution.Qhd1440p: return 1440;
                case VideoResolution.Fhd1080p: return 1080;
                case VideoResolution.Hd720p: return 720;
                case VideoResolution.Sd480p: return 480;
                case VideoResolution.Low360p: return 360;
                default: return 0; // Best / Uncapped
            }
        }

        private string BuildYtDlpArguments(DownloadQueueItem item)
        {
            string outputTemplate = !string.IsNullOrEmpty(item.CustomBaseFileName)
                ? Path.Combine(item.OutputFolder, item.CustomBaseFileName.Replace("%", "%%") + ".%(ext)s")
                : Path.Combine(item.OutputFolder, "%(title).180B.%(ext)s");

            StringBuilder args = new StringBuilder();
            args.Append("--newline --no-colors --progress --no-playlist ");

            // Multi-Connection Turbo Downloading: concurrent DASH/HLS fragments + chunked HTTP Range requests to bypass single-stream server throttling
            if (item.ConcurrentFragments > 1)
            {
                args.AppendFormat("--concurrent-fragments {0} --buffer-size 16K --http-chunk-size 10M ", item.ConcurrentFragments);
            }

            if (File.Exists(ffmpegPath))
            {
                string ffmpegDir = Path.GetDirectoryName(ffmpegPath);
                args.AppendFormat("--ffmpeg-location \"{0}\" ", ffmpegDir);
            }

            int heightCap = GetResolutionHeightCap(item.Resolution);

            switch (item.Mode)
            {
                case DownloadMode.BestVideoAndAudioMp4:
                {
                    // Adaptive Smart Muxing Without Re-encoding:
                    // 1) For <= 1080p caps: prioritize native H.264 (avc) + AAC (mp4a/m4a) for instant 1-second FFmpeg stream copy and universal compatibility, with safe fallbacks.
                    // 2) For Best / 1440p / 4K: sort by highest resolution first, then prefer H.264/AAC at that resolution, and always prefer native AAC audio (m4a) so FFmpeg never has to re-encode audio.
                    string mp4Filter;
                    string mp4Sort;
                    if (heightCap > 0 && heightCap <= 1080)
                    {
                        mp4Filter = string.Format(
                            "bv*[vcodec^=avc][height<={0}]+ba[acodec^=mp4a]/bv*[ext=mp4][height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b",
                            heightCap);
                        mp4Sort = string.Format("res:{0},vcodec:h264,acodec:aac,ext:mp4:m4a", heightCap);
                    }
                    else if (heightCap > 1080)
                    {
                        mp4Filter = string.Format(
                            "bv*[height<={0}]+ba[acodec^=mp4a]/bv*[height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b",
                            heightCap);
                        mp4Sort = string.Format("res:{0},vcodec:h264,acodec:aac,ext:mp4:m4a", heightCap);
                    }
                    else
                    {
                        mp4Filter = "bv*+ba[acodec^=mp4a]/bv*+ba[ext=m4a]/bv*+ba/b";
                        mp4Sort = "res,vcodec:h264,acodec:aac,ext:mp4:m4a";
                    }
                    args.AppendFormat("-f \"{0}\" -S \"{1}\" --merge-output-format mp4 ", mp4Filter, mp4Sort);
                    break;
                }
                case DownloadMode.BestVideoAndAudioMkv:
                {
                    string mkvFilter = heightCap > 0
                        ? string.Format("bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b", heightCap)
                        : "bv*+ba/b";
                    args.AppendFormat("-f \"{0}\" --merge-output-format mkv ", mkvFilter);
                    break;
                }
                case DownloadMode.BestAudioOnlyMp3:
                    args.Append("-f \"ba/b\" -x --audio-format mp3 --audio-quality 0 ");
                    break;
                case DownloadMode.BestAudioOnlyM4a:
                    // Prefer native AAC/m4a stream so FFmpeg can fast-copy without re-encoding
                    args.Append("-f \"ba[acodec^=mp4a]/ba[ext=m4a]/ba/b\" -x --audio-format m4a --audio-quality 0 ");
                    break;
                case DownloadMode.BestAudioOnlyFlac:
                    args.Append("-f \"ba/b\" -x --audio-format flac --audio-quality 0 ");
                    break;
            }

            if (item.EmbedMetadata)
            {
                args.Append("--embed-metadata --embed-chapters ");
            }

            if (item.EmbedThumbnail)
            {
                args.Append("--embed-thumbnail --convert-thumbnails jpg ");
            }

            args.AppendFormat("-o \"{0}\" ", outputTemplate);
            args.AppendFormat("\"{0}\"", item.Url.Replace("\"", ""));

            return args.ToString();
        }

        private async Task ExecuteDownloadAsync(DownloadQueueItem item)
        {
            try
            {
                Tuple<string, string> meta = await FetchMetadataTitleAndIdAsync(item);

                if (item.CancellationSource.IsCancellationRequested)
                {
                    MarkCancelled(item);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(meta.Item1))
                {
                    bool proceed = CheckAndPromptForDuplicateRename(item, meta.Item1, meta.Item2);
                    if (!proceed)
                    {
                        MarkCancelled(item);
                        return;
                    }
                    RefreshQueueUi();
                }

                item.Status = QueueItemStatus.Downloading;
                item.StatusText = "Starting...";
                RefreshQueueUi();

                string arguments = BuildYtDlpArguments(item);
                AppendLog(string.Format("[{0}] yt-dlp {1}", item.Id, arguments));

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    item.ActiveProcess = proc;

                    proc.OutputDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            ParseYtDlpOutput(item, e.Data);
                        }
                    };

                    proc.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            AppendLog(string.Format("[{0} ERR] {1}", item.Id, e.Data));
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    while (!proc.HasExited)
                    {
                        if (item.CancellationSource.IsCancellationRequested)
                        {
                            KillProcessTree(proc.Id);
                            MarkCancelled(item);
                            return;
                        }
                        await Task.Delay(150);
                    }

                    proc.WaitForExit();

                    if (item.CancellationSource.IsCancellationRequested)
                    {
                        MarkCancelled(item);
                    }
                    else if (proc.ExitCode == 0)
                    {
                        item.ProgressPercent = 100.0;
                        item.Speed = "—";
                        item.Eta = "00:00";
                        item.Status = QueueItemStatus.Completed;
                        item.StatusText = "Completed";
                        RefreshQueueUi();
                        AppendLog(string.Format("[{0}] Completed: {1}", item.Id, item.Title));
                    }
                    else
                    {
                        item.Status = QueueItemStatus.Error;
                        item.StatusText = "Failed (Exit " + proc.ExitCode + ")";
                        RefreshQueueUi();
                        AppendLog(string.Format("[{0}] Failed with exit code {1}", item.Id, proc.ExitCode));
                    }
                }
            }
            catch (Exception ex)
            {
                if (item.CancellationSource.IsCancellationRequested)
                {
                    MarkCancelled(item);
                }
                else
                {
                    item.Status = QueueItemStatus.Error;
                    item.StatusText = "Error";
                    RefreshQueueUi();
                    AppendLog(string.Format("[{0}] Error: {1}", item.Id, ex.Message));
                }
            }
            finally
            {
                item.ActiveProcess = null;
                lock (queueLock)
                {
                    activeDownloads = Math.Max(0, activeDownloads - 1);
                }
                BeginInvoke((Action)(() =>
                {
                    UpdateQueueHeaderStats();
                    ProcessQueue();
                }));
            }
        }

        private async Task<Tuple<string, string>> FetchMetadataTitleAndIdAsync(DownloadQueueItem item)
        {
            string fetchedTitle = "";
            string fetchedId = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = string.Format("--no-playlist --get-title --get-id --no-warnings \"{0}\"", item.Url.Replace("\"", "")),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(psi))
                {
                    Task<string> readAllTask = p.StandardOutput.ReadToEndAsync();
                    Task completed = await Task.WhenAny(readAllTask, Task.Delay(9000));
                    if (completed == readAllTask)
                    {
                        string raw = await readAllTask;
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length >= 1)
                            {
                                fetchedTitle = lines[0].Trim();
                                item.Title = fetchedTitle;
                                RefreshQueueUi();
                            }
                            if (lines.Length >= 2)
                            {
                                fetchedId = lines[1].Trim();
                            }
                        }
                    }
                    else
                    {
                        KillProcessTree(p.Id);
                    }
                }
            }
            catch { }
            return Tuple.Create(fetchedTitle, fetchedId);
        }

        private void ParseYtDlpOutput(DownloadQueueItem item, string line)
        {
            Match destMatch = DestinationRegex.Match(line);
            if (destMatch.Success)
            {
                string path = destMatch.Groups["path"].Value.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    item.OutputFilePath = path;
                    if (item.Title == item.Url)
                    {
                        item.Title = Path.GetFileNameWithoutExtension(path);
                    }
                }
            }

            if (line.StartsWith("[Merger]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[ExtractAudio]", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = QueueItemStatus.Merging;
                item.StatusText = "FFmpeg Muxing...";
                item.ProgressPercent = 99.0;
                RefreshQueueUi();
                AppendLog(string.Format("[{0}] {1}", item.Id, line));
                return;
            }

            if (line.StartsWith("[Metadata]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[ThumbnailsConvertor]", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = QueueItemStatus.Merging;
                item.StatusText = "Embedding Tags & Art...";
                item.ProgressPercent = 99.6;
                RefreshQueueUi();
                AppendLog(string.Format("[{0}] {1}", item.Id, line));
                return;
            }

            Match progMatch = ProgressRegex.Match(line);
            if (progMatch.Success)
            {
                double pct;
                if (double.TryParse(progMatch.Groups["percent"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out pct))
                {
                    item.ProgressPercent = pct;
                }

                if (progMatch.Groups["speed"].Success && !string.IsNullOrWhiteSpace(progMatch.Groups["speed"].Value))
                {
                    item.Speed = progMatch.Groups["speed"].Value;
                }

                if (progMatch.Groups["eta"].Success && !string.IsNullOrWhiteSpace(progMatch.Groups["eta"].Value))
                {
                    item.Eta = progMatch.Groups["eta"].Value;
                }

                item.Status = QueueItemStatus.Downloading;
                item.StatusText = "Downloading";
                RefreshQueueUi();
            }
        }

        private void MarkCancelled(DownloadQueueItem item)
        {
            item.Status = QueueItemStatus.Cancelled;
            item.StatusText = "Cancelled";
            item.Speed = "—";
            item.Eta = "—";
            RefreshQueueUi();
            AppendLog(string.Format("[{0}] Cancelled: {1}", item.Id, item.Title));
        }

        private void KillProcessTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/F /T /PID " + pid)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(2000);
                }
            }
            catch { }
        }

        private void CancelSingleItem(DownloadQueueItem item)
        {
            if (item == null) return;
            if (item.Status == QueueItemStatus.Queued ||
                item.Status == QueueItemStatus.FetchingInfo ||
                item.Status == QueueItemStatus.Downloading ||
                item.Status == QueueItemStatus.Merging)
            {
                item.CancellationSource.Cancel();
                if (item.Status == QueueItemStatus.Queued)
                {
                    MarkCancelled(item);
                }
                else if (item.ActiveProcess != null && !item.ActiveProcess.HasExited)
                {
                    KillProcessTree(item.ActiveProcess.Id);
                }
            }
        }

        private void OpenCompletedItemFolder(DownloadQueueItem item)
        {
            if (item == null) return;
            try
            {
                if (!string.IsNullOrEmpty(item.OutputFilePath) && File.Exists(item.OutputFilePath))
                {
                    Process.Start("explorer.exe", "/select,\"" + item.OutputFilePath + "\"");
                }
                else if (Directory.Exists(item.OutputFolder))
                {
                    Process.Start("explorer.exe", item.OutputFolder);
                }
            }
            catch { }
        }

        private void BtnCancelSelected_Click(object sender, EventArgs e)
        {
            CancelSingleItem(queueListView.SelectedItem);
        }

        private void BtnRemoveFinished_Click(object sender, EventArgs e)
        {
            List<DownloadQueueItem> current = queueListView.GetItems();
            foreach (DownloadQueueItem item in current)
            {
                if (item.Status == QueueItemStatus.Completed ||
                    item.Status == QueueItemStatus.Cancelled ||
                    item.Status == QueueItemStatus.Error)
                {
                    lock (queueLock)
                    {
                        queue.Remove(item);
                    }
                    queueListView.RemoveItem(item);
                }
            }
            UpdateQueueHeaderStats();
        }

        private void RefreshQueueUi()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)RefreshQueueUi);
                return;
            }
            queueListView.Invalidate();
            UpdateQueueHeaderStats();
        }

        private void AppendLog(string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => AppendLog(message)));
                return;
            }

            string line = string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine);
            txtLog.AppendText(line);
            txtLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            settings.Save();
            lock (queueLock)
            {
                foreach (var item in queue)
                {
                    try
                    {
                        item.CancellationSource.Cancel();
                        if (item.ActiveProcess != null && !item.ActiveProcess.HasExited)
                        {
                            KillProcessTree(item.ActiveProcess.Id);
                        }
                    }
                    catch { }
                }
            }
            base.OnFormClosing(e);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
