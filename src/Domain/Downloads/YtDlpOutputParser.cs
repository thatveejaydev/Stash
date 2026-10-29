using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MediaDownloader
{
    public static class YtDlpOutputParser
    {
        private static readonly Regex ProgressRegex = new Regex(
            @"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%\s+of\s+~?\s*(?<size>\S+)(?:\s+at\s+(?<speed>\S+))?(?:\s+ETA\s+(?<eta>\S+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DestinationRegex = new Regex(
            @"\[(?:download|Merger|ExtractAudio)\]\s+(?:Destination:\s+|Merging formats into\s+""?)(?<path>[^""\r\n]+)""?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static void ParseLine(DownloadQueueItem item, string line, Action onUiChanged, Action<string> onLogLine)
        {
            Match destMatch = DestinationRegex.Match(line);
            if (destMatch.Success)
            {
                string path = destMatch.Groups["path"].Value.Trim();
                if (!string.IsNullOrEmpty(path))
                {
                    item.OutputFilePath = path;
                    lock (item.TrackedFilePaths)
                    {
                        if (!item.TrackedFilePaths.Contains(path))
                        {
                            item.TrackedFilePaths.Add(path);
                        }
                    }
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
                if (onUiChanged != null) onUiChanged();
                if (onLogLine != null) onLogLine(string.Format("[{0}] {1}", item.Id, line));
                return;
            }

            if (line.StartsWith("[Metadata]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[EmbedThumbnail]", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("[ThumbnailsConvertor]", StringComparison.OrdinalIgnoreCase))
            {
                item.Status = QueueItemStatus.Merging;
                item.StatusText = "Embedding Tags & Art...";
                item.ProgressPercent = 99.6;
                if (onUiChanged != null) onUiChanged();
                if (onLogLine != null) onLogLine(string.Format("[{0}] {1}", item.Id, line));
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
                if (onUiChanged != null) onUiChanged();
            }
        }
    }
}
