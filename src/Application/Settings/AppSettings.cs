using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;

namespace Stash
{
    // Persistent Settings (Remembers Save-To Directory History & User Preferences)
    public class AppSettings
    {
        private const string DownloadsToken = "%DOWNLOADS%";

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

        public static string GetDownloadsFolder()
        {
            Guid downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            IntPtr path = IntPtr.Zero;
            try
            {
                // Ask Windows, so redirected Downloads folders are respected too.
                if (SHGetKnownFolderPath(ref downloadsId, 0, IntPtr.Zero, out path) == 0)
                {
                    string folder = Marshal.PtrToStringUni(path);
                    if (!string.IsNullOrWhiteSpace(folder)) return folder;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            finally { if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path); }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static string ResolveSavedFolder(string folder)
        {
            if (string.Equals(folder, DownloadsToken, StringComparison.OrdinalIgnoreCase))
                return GetDownloadsFolder();
            // Ignore stale paths copied from another computer.
            return Path.IsPathRooted(folder) && Directory.Exists(folder) ? folder : null;
        }

        private static string SerializeFolder(string folder)
        {
            return string.Equals(folder.TrimEnd('\\', '/'), GetDownloadsFolder().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                ? DownloadsToken : folder;
        }
        public string CurrentOutputFolder { get; set; }
        public List<string> RecentFolders { get; set; }
        private int formatIndex;
        public int FormatIndex
        {
            get { return formatIndex; }
            set
            {
                formatIndex = value;
                DownloadMode mode = (DownloadMode)value;
                if (IsVideoMode(mode)) VideoFormat = mode;
                if (IsAudioMode(mode)) AudioFormat = mode;
            }
        }
        internal DownloadMode VideoFormat { get; private set; }
        internal DownloadMode AudioFormat { get; private set; }
        internal DownloadKind SelectedKind
        {
            get
            {
                DownloadMode mode = (DownloadMode)FormatIndex;
                if (mode == DownloadMode.TranscriptOnly) return DownloadKind.Transcript;
                return IsAudioMode(mode) ? DownloadKind.Audio : DownloadKind.Video;
            }
            set
            {
                FormatIndex = (int)(value == DownloadKind.Transcript ? DownloadMode.TranscriptOnly :
                    value == DownloadKind.Audio ? AudioFormat : VideoFormat);
            }
        }

        private static bool IsVideoMode(DownloadMode mode)
        {
            return mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv;
        }

        private static bool IsAudioMode(DownloadMode mode)
        {
            return mode >= DownloadMode.BestAudioOnlyMp3 && mode <= DownloadMode.BestAudioOnlyWav;
        }
        public int QualityIndex { get; set; }
        internal TranscriptFormat TranscriptFormat { get; set; }
        internal string TranscriptLanguage { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EmbedThumbnail { get; set; }
        public int ParallelCount { get; set; }
        public int ConcurrentFragments { get; set; }
        public bool EmbedSubtitles { get; set; }
        public bool IncludeAutoCaptions { get; set; }
        internal CookieBrowser BrowserCookies { get; set; }
        internal FileNameStyle FileNameStyle { get; set; }
        public bool WatchClipboard { get; set; }
        public DateTime LastEngineCheckUtc { get; set; }

        private readonly string settingsPath;

        public AppSettings() : this(AppPaths.Current.SettingsPath) { }

        internal AppSettings(string settingsPath)
        {
            this.settingsPath = settingsPath;
            string defaultDownloads = GetDownloadsFolder();

            CurrentOutputFolder = defaultDownloads;
            RecentFolders = new List<string> { defaultDownloads };
            FormatIndex = 0;
            AudioFormat = DownloadMode.BestAudioOnlyMp3;
            QualityIndex = 0;
            EmbedMetadata = true;
            EmbedThumbnail = true;
            ParallelCount = 2;
            ConcurrentFragments = 8; // Default 8x Multi-Connection Turbo
            EmbedSubtitles = true;
            IncludeAutoCaptions = true;
        }

        public void AddFolderToHistory(string folder)
        {
            if (AddFolderToHistoryCore(folder)) Save();
        }

        // Returns true when the history changed and needs saving.
        private bool AddFolderToHistoryCore(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return false;
            folder = folder.Trim();
            if (RecentFolders.Count > 0 && string.Equals(RecentFolders[0], folder, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(CurrentOutputFolder, folder, StringComparison.OrdinalIgnoreCase))
                return false;
            InsertFolder(folder);
            return true;
        }

        private void InsertFolder(string folder)
        {
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
                string defaultDownloads = GetDownloadsFolder();
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
            return Load(AppPaths.Current.SettingsPath);
        }

        internal static AppSettings Load(string settingsPath)
        {
            AppSettings s = new AppSettings(settingsPath);
            try
            {
                string path = settingsPath;
                if (!File.Exists(path)) { s.Save(); return s; }

                List<string> loadedHistory = new List<string>();
                DownloadMode? savedVideoFormat = null, savedAudioFormat = null;
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
                    CookieBrowser parsedBrowser;
                    FileNameStyle parsedStyle;
                    TranscriptFormat parsedTranscriptFormat;
                    long parsedTicks;
                    if (key == "CurrentOutputFolder" && !string.IsNullOrEmpty(val))
                        s.CurrentOutputFolder = ResolveSavedFolder(val) ?? GetDownloadsFolder();
                    else if (key == "RecentFolder" && !string.IsNullOrEmpty(val))
                    {
                        string folder = ResolveSavedFolder(val);
                        if (folder != null) loadedHistory.Add(folder);
                    }
                    else if (key == "FormatIndex" && int.TryParse(val, out parsedInt))
                        s.FormatIndex = Enum.IsDefined(typeof(DownloadMode), parsedInt) ? parsedInt : 0;
                    else if (key == "VideoFormat" && int.TryParse(val, out parsedInt) && IsVideoMode((DownloadMode)parsedInt))
                        savedVideoFormat = (DownloadMode)parsedInt;
                    else if (key == "AudioFormat" && int.TryParse(val, out parsedInt) && IsAudioMode((DownloadMode)parsedInt))
                        savedAudioFormat = (DownloadMode)parsedInt;
                    else if (key == "QualityIndex" && int.TryParse(val, out parsedInt))
                        s.QualityIndex = Math.Max(0, Math.Min(6, parsedInt));
                    else if (key == "TranscriptLanguage" && TranscriptCaptions.IsLanguageCode(val))
                        s.TranscriptLanguage = val;
                    else if (key == "TranscriptFormat" && Enum.TryParse(val, true, out parsedTranscriptFormat) &&
                        Enum.IsDefined(typeof(TranscriptFormat), parsedTranscriptFormat))
                        s.TranscriptFormat = parsedTranscriptFormat;
                    else if (key == "EmbedMetadata" && bool.TryParse(val, out parsedBool))
                        s.EmbedMetadata = parsedBool;
                    else if (key == "EmbedThumbnail" && bool.TryParse(val, out parsedBool))
                        s.EmbedThumbnail = parsedBool;
                    else if (key == "ParallelCount" && int.TryParse(val, out parsedInt))
                        s.ParallelCount = Math.Max(1, Math.Min(6, parsedInt));
                    else if (key == "ConcurrentFragments" && int.TryParse(val, out parsedInt))
                        s.ConcurrentFragments = Math.Max(1, Math.Min(16, parsedInt));
                    else if (key == "EmbedSubtitles" && bool.TryParse(val, out parsedBool))
                        s.EmbedSubtitles = parsedBool;
                    else if (key == "IncludeAutoCaptions" && bool.TryParse(val, out parsedBool))
                        s.IncludeAutoCaptions = parsedBool;
                    else if (key == "BrowserCookies" && Enum.TryParse(val, true, out parsedBrowser) &&
                        Enum.IsDefined(typeof(CookieBrowser), parsedBrowser))
                        s.BrowserCookies = parsedBrowser;
                    else if (key == "FileNameStyle" && Enum.TryParse(val, true, out parsedStyle) &&
                        Enum.IsDefined(typeof(FileNameStyle), parsedStyle))
                        s.FileNameStyle = parsedStyle;
                    else if (key == "WatchClipboard" && bool.TryParse(val, out parsedBool))
                        s.WatchClipboard = parsedBool;
                    else if (key == "LastEngineCheckUtc" && long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedTicks) &&
                        parsedTicks >= DateTime.MinValue.Ticks && parsedTicks <= DateTime.MaxValue.Ticks)
                        s.LastEngineCheckUtc = new DateTime(parsedTicks, DateTimeKind.Utc);
                }

                if (savedVideoFormat.HasValue && s.SelectedKind != DownloadKind.Video) s.VideoFormat = savedVideoFormat.Value;
                if (savedAudioFormat.HasValue && s.SelectedKind != DownloadKind.Audio) s.AudioFormat = savedAudioFormat.Value;
                if (loadedHistory.Count > 0)
                {
                    s.RecentFolders = loadedHistory;
                }
                // Loading never rewrites the file; it is saved when something actually changes.
                s.AddFolderToHistoryCore(s.CurrentOutputFolder);
            }
            catch (Exception ex) { AppLog.Error("Could not read settings", ex); }
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CurrentOutputFolder=" + SerializeFolder(CurrentOutputFolder));
                sb.AppendLine("FormatIndex=" + FormatIndex);
                sb.AppendLine("VideoFormat=" + (int)VideoFormat);
                sb.AppendLine("AudioFormat=" + (int)AudioFormat);
                sb.AppendLine("QualityIndex=" + QualityIndex);
                sb.AppendLine("TranscriptLanguage=" + TranscriptLanguage);
                sb.AppendLine("TranscriptFormat=" + TranscriptFormat);
                sb.AppendLine("EmbedMetadata=" + EmbedMetadata);
                sb.AppendLine("EmbedThumbnail=" + EmbedThumbnail);
                sb.AppendLine("ParallelCount=" + ParallelCount);
                sb.AppendLine("ConcurrentFragments=" + ConcurrentFragments);
                sb.AppendLine("EmbedSubtitles=" + EmbedSubtitles);
                sb.AppendLine("IncludeAutoCaptions=" + IncludeAutoCaptions);
                sb.AppendLine("BrowserCookies=" + BrowserCookies);
                sb.AppendLine("FileNameStyle=" + FileNameStyle);
                sb.AppendLine("WatchClipboard=" + WatchClipboard);
                sb.AppendLine("LastEngineCheckUtc=" + LastEngineCheckUtc.Ticks.ToString(CultureInfo.InvariantCulture));
                foreach (string dir in RecentFolders)
                {
                    sb.AppendLine("RecentFolder=" + SerializeFolder(dir));
                }
                AtomicFile.WriteAllText(settingsPath, sb.ToString());
            }
            catch (Exception ex) { AppLog.Error("Could not save settings", ex); }
        }
    }
}
