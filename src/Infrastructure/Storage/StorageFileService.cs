using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MediaDownloader
{
    public static class StorageFileService
    {
        private const int MaxSafeBaseFileNameLength = 100;

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

        public static string FindSimilarExistingFile(string folder, string cleanTitle, string videoId)
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

        public static string SuggestNextAvailableName(string folder, string baseTitle, string ext)
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

        public static async Task<int> CleanupPartialFilesForItemAsync(DownloadQueueItem item)
        {
            if (item == null) return 0;
            string folder = Path.GetFullPath(item.TemporaryFolder);
            string parent = Path.GetFullPath(item.OutputFolder).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(folder), parent, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(folder).StartsWith(".stash-temp-", StringComparison.Ordinal))
                throw new InvalidOperationException("Invalid temporary folder.");
            int deleted = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(150);
                try
                {
                    if (!Directory.Exists(folder)) break;
                    // Never follow junctions or recursively delete unknown subdirectories.
                    if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) break;
                    foreach (string file in Directory.GetFiles(folder))
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                        try { File.Delete(file); deleted++; }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    Directory.Delete(folder, false);
                    break;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }
    }
}
