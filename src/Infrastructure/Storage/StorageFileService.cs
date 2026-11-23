using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Stash
{
    public static class StorageFileService
    {
        internal const int MaxSafeBaseFileNameLength = 100;
        internal const string TemporaryFolderPrefix = ".stash-temp-";

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            StringBuilder sb = new StringBuilder(name.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            bool lastWasSpace = false;

            foreach (char c in name)
            {
                if (c == '\uFFFD' || char.IsControl(c) || char.IsSurrogate(c))
                {
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                    continue;
                }

                lastWasSpace = false;
                if (Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            string cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
            if (cleaned.Length > MaxSafeBaseFileNameLength)
            {
                cleaned = cleaned.Substring(0, MaxSafeBaseFileNameLength).Trim().TrimEnd('.', ' ', '_');
            }
            return cleaned;
        }

        // Normalizes a directory for comparisons: full path without trailing separators,
        // except for a drive or share root, which keeps its separator ("D:\").
        internal static string NormalizeDirectory(string path)
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full;
        }

        internal static bool SameDirectory(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            try { return string.Equals(NormalizeDirectory(first), NormalizeDirectory(second), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        private static bool IsPartialFile(string file)
        {
            string ext = Path.GetExtension(file);
            return string.Equals(ext, ".part", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".ytdl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".temp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".tmp", StringComparison.OrdinalIgnoreCase);
        }

        // Finds an existing download with the same base name, or one tagged with the same [video id].
        // Uses native wildcard filtering so large folders are not enumerated in managed code.
        public static string FindSimilarExistingFile(string folder, string cleanTitle, string videoId)
        {
            if (!Directory.Exists(folder)) return null;
            try
            {
                if (!string.IsNullOrEmpty(cleanTitle))
                {
                    foreach (string file in Directory.EnumerateFiles(folder, cleanTitle + ".*"))
                    {
                        if (IsPartialFile(file)) continue;
                        if (string.Equals(Path.GetFileNameWithoutExtension(file), cleanTitle, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFileName(file);
                    }
                    foreach (string file in Directory.EnumerateFiles(folder, cleanTitle + " [*]*"))
                    {
                        if (IsPartialFile(file)) continue;
                        string stripped = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"\s*\[[A-Za-z0-9_\-]{5,20}\]$", "").Trim();
                        if (string.Equals(stripped, cleanTitle, StringComparison.OrdinalIgnoreCase))
                            return Path.GetFileName(file);
                    }
                }

                if (!string.IsNullOrEmpty(videoId) && Regex.IsMatch(videoId, @"^[A-Za-z0-9_\-]{1,64}$"))
                {
                    foreach (string file in Directory.EnumerateFiles(folder, "*[" + videoId + "]*"))
                    {
                        if (IsPartialFile(file)) continue;
                        if (Path.GetFileNameWithoutExtension(file).IndexOf("[" + videoId + "]", StringComparison.OrdinalIgnoreCase) >= 0)
                            return Path.GetFileName(file);
                    }
                }
            }
            catch (IOException ex) { AppLog.Error("Duplicate check failed", ex); }
            catch (UnauthorizedAccessException ex) { AppLog.Error("Duplicate check failed", ex); }
            catch (ArgumentException ex) { AppLog.Error("Duplicate check failed", ex); }
            return null;
        }

        public static string SuggestNextAvailableName(string folder, string baseTitle, string ext)
        {
            return SuggestNextAvailableName(folder, baseTitle, ext, null);
        }

        internal static string SuggestNextAvailableName(string folder, string baseTitle, string ext, Func<string, bool> reserved)
        {
            int counter = 2;
            while (counter < 999)
            {
                string candidate = string.Format("{0} ({1})", baseTitle, counter);
                string fullPath = Path.Combine(folder, candidate + ext);
                if (!File.Exists(fullPath) && (reserved == null || !reserved(candidate)))
                {
                    return candidate;
                }
                counter++;
            }
            return baseTitle + " (Copy)";
        }

        private static string GetValidatedTemporaryFolder(DownloadQueueItem item)
        {
            string folder = Path.GetFullPath(item.TemporaryFolder);
            if (!SameDirectory(Path.GetDirectoryName(folder), item.OutputFolder) ||
                !Path.GetFileName(folder).StartsWith(TemporaryFolderPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid temporary folder.");
            return folder;
        }

        internal static void CreateTemporaryFolder(DownloadQueueItem item)
        {
            string folder = GetValidatedTemporaryFolder(item);
            DirectoryInfo info = Directory.CreateDirectory(folder);
            try
            {
                // Keep in-progress fragments out of the way in Explorer.
                if ((info.Attributes & FileAttributes.Hidden) == 0) info.Attributes |= FileAttributes.Hidden;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string FindTranscriptCaption(DownloadQueueItem item, string extension)
        {
            string folder = GetValidatedTemporaryFolder(item);
            string[] files = Directory.GetFiles(folder, "caption.*" + extension);
            if (files.Length != 1 || new FileInfo(files[0]).Length == 0)
                throw new IOException("The engine did not save a usable caption file.");
            return files[0];
        }

        internal static string PublishTranscript(DownloadQueueItem item, string sourcePath, string baseName, string extension)
        {
            string stem = baseName.Split('.')[0];
            if (Regex.IsMatch(stem, @"\A(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                baseName = "_" + baseName;
            for (int copy = 1; copy <= 999; copy++)
            {
                string name = copy == 1 ? baseName : baseName + " (" + copy + ")";
                string destination = Path.Combine(item.OutputFolder, name + extension);
                try
                {
                    File.Move(sourcePath, destination);
                    return destination;
                }
                catch (IOException)
                {
                    if (!File.Exists(destination) && !Directory.Exists(destination)) throw;
                }
            }
            throw new IOException("Could not find an available name for the transcript.");
        }

        public static Task CleanupEmptyTemporaryFolderAsync(DownloadQueueItem item)
        {
            string folder = GetValidatedTemporaryFolder(item);
            return Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(folder) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return;
                    // Non-recursive deletion preserves all partial files and unknown contents.
                    Directory.Delete(folder, false);
                }
                catch (IOException) { } // Non-empty folder, or a file is still locked.
                catch (UnauthorizedAccessException) { }
            });
        }

        public static async Task<int> CleanupPartialFilesForItemAsync(DownloadQueueItem item)
        {
            if (item == null) return 0;
            string folder = GetValidatedTemporaryFolder(item);
            int deleted = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(150);
                try
                {
                    if (!Directory.Exists(folder)) break;
                    deleted += DeleteTemporaryFolderContents(folder);
                    break;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }

        // Deletes the files of one Stash temporary folder and then the folder itself.
        // Never follows junctions or recursively deletes unknown subdirectories.
        private static int DeleteTemporaryFolderContents(string folder)
        {
            int deleted = 0;
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return 0;
            foreach (string file in Directory.GetFiles(folder))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                try { File.Delete(file); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Directory.Delete(folder, false);
            return deleted;
        }

        // Removes temporary folders left behind by earlier sessions (for example after a crash).
        // Folders still referenced by a restored queue item, or touched recently, are kept.
        internal static int SweepOrphanedTemporaryFolders(IEnumerable<string> outputFolders, ICollection<string> inUse, TimeSpan minimumAge)
        {
            int removed = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string outputFolder in outputFolders)
            {
                if (string.IsNullOrWhiteSpace(outputFolder)) continue;
                string normalized;
                try { normalized = NormalizeDirectory(outputFolder); }
                catch (Exception) { continue; }
                if (!seen.Add(normalized) || !Directory.Exists(normalized)) continue;
                string[] candidates;
                try { candidates = Directory.GetDirectories(normalized, TemporaryFolderPrefix + "*"); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (string candidate in candidates)
                {
                    try
                    {
                        string full = Path.GetFullPath(candidate);
                        bool referenced = false;
                        foreach (string used in inUse)
                            if (string.Equals(Path.GetFullPath(used), full, StringComparison.OrdinalIgnoreCase)) { referenced = true; break; }
                        if (referenced) continue;
                        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(full) < minimumAge) continue;
                        if (Directory.GetDirectories(full).Length > 0) continue;
                        DeleteTemporaryFolderContents(full);
                        removed++;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return removed;
        }
    }
}
