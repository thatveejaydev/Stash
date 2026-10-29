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
            if (item == null || string.IsNullOrEmpty(item.OutputFolder) || !Directory.Exists(item.OutputFolder))
                return 0;

            List<string> basePrefixes = new List<string>();
            if (!string.IsNullOrEmpty(item.CustomBaseFileName))
            {
                basePrefixes.Add(item.CustomBaseFileName);
            }
            if (!string.IsNullOrEmpty(item.Title) && item.Title != item.Url)
            {
                string sanitized = SanitizeFileName(item.Title);
                if (!string.IsNullOrEmpty(sanitized) && !basePrefixes.Contains(sanitized))
                {
                    basePrefixes.Add(sanitized);
                }
            }
            lock (item.TrackedFilePaths)
            {
                foreach (string tracked in item.TrackedFilePaths)
                {
                    string fileName = Path.GetFileName(tracked);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        basePrefixes.Add(fileName);
                        string noExt = Path.GetFileNameWithoutExtension(fileName);
                        if (!string.IsNullOrEmpty(noExt)) basePrefixes.Add(noExt);
                    }
                }
            }

            int deletedCount = 0;

            // Retry a few times to allow killed yt-dlp/ffmpeg child processes to release OS file handles
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(150);
                try
                {
                    string[] allFiles = Directory.GetFiles(item.OutputFolder);
                    foreach (string fullPath in allFiles)
                    {
                        string fileName = Path.GetFileName(fullPath);
                        if (string.IsNullOrEmpty(fileName)) continue;

                        bool isTempOrFragment =
                            fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith(".temp", StringComparison.OrdinalIgnoreCase) ||
                            fileName.IndexOf(".part-Frag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            Regex.IsMatch(fileName, @"\.f[0-9]+\.[a-zA-Z0-9]+(\.part.*)?$", RegexOptions.IgnoreCase);

                        if (!isTempOrFragment) continue;

                        bool matchesItem = false;
                        foreach (string prefix in basePrefixes)
                        {
                            if (!string.IsNullOrEmpty(prefix) &&
                                fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                matchesItem = true;
                                break;
                            }
                        }

                        if (matchesItem)
                        {
                            try
                            {
                                File.Delete(fullPath);
                                deletedCount++;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return deletedCount;
        }
    }
}
