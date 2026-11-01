using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace MediaDownloader
{
    public sealed class CompletedDownloadDetails
    {
        public double? DurationSeconds { get; private set; }
        public long? FileSizeBytes { get; private set; }
        public string Container { get; private set; }
        public int? Width { get; private set; }
        public int? Height { get; private set; }
        public double? FramesPerSecond { get; private set; }

        internal static CompletedDownloadDetails Parse(string fields)
        {
            string[] parts = fields.Split('|');
            if (parts.Length != 5) return null;
            double? duration = PositiveNumber(parts[0]);
            double? width = PositiveNumber(parts[1]);
            double? height = PositiveNumber(parts[2]);
            return new CompletedDownloadDetails
            {
                DurationSeconds = duration.HasValue && duration.Value <= int.MaxValue ? duration : null,
                Width = width.HasValue && width.Value <= int.MaxValue ? (int?)width.Value : null,
                Height = height.HasValue && height.Value <= int.MaxValue ? (int?)height.Value : null,
                FramesPerSecond = PositiveNumber(parts[3]),
                Container = parts[4] == "NA" ? null : parts[4].Trim().ToUpperInvariant()
            };
        }

        private static double? PositiveNumber(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 ? (double?)value : null;
        }

        internal static Task<CompletedDownloadDetails> ReadFileAsync(string path, CompletedDownloadDetails metadata)
        {
            return Task.Run(() =>
            {
                CompletedDownloadDetails details = metadata == null ? new CompletedDownloadDetails() : (CompletedDownloadDetails)metadata.MemberwiseClone();
                if (string.IsNullOrWhiteSpace(path)) return details;
                try
                {
                    FileInfo file = new FileInfo(path);
                    if (file.Exists)
                    {
                        details.FileSizeBytes = file.Length;
                        details.Container = file.Extension.TrimStart('.').ToUpperInvariant();
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                return details;
            });
        }

        public string GetSummary(DownloadMode mode)
        {
            List<string> parts = new List<string>();
            if (DurationSeconds.HasValue)
            {
                long seconds = (long)DurationSeconds.Value;
                parts.Add(seconds >= 3600
                    ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60)
                    : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds / 60, seconds % 60));
            }
            if (FileSizeBytes.HasValue)
            {
                double size = FileSizeBytes.Value;
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                int unit = 0;
                while (size >= 1000 && unit < units.Length - 1) { size /= 1000; unit++; }
                parts.Add(size.ToString(unit == 0 ? "0" : "0.#", CultureInfo.InvariantCulture) + " " + units[unit]);
            }
            parts.Add(string.IsNullOrWhiteSpace(Container) ? YtDlpCommandBuilder.GetTargetExtension(mode).TrimStart('.').ToUpperInvariant() : Container);
            bool video = mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv;
            if (video && Width.HasValue && Height.HasValue)
            {
                int shortSide = Math.Min(Width.Value, Height.Value);
                string quality = shortSide >= 4320 ? "8K" : shortSide >= 2160 ? "4K" : shortSide >= 1440 ? "QHD" : shortSide >= 1080 ? "Full HD" : shortSide >= 720 ? "HD" : null;
                parts.Add(Width + "×" + Height + (quality == null ? "" : " (" + quality + ")"));
            }
            if (video && FramesPerSecond.HasValue)
                parts.Add(FramesPerSecond.Value.ToString("0.##", CultureInfo.InvariantCulture) + " fps");
            if (parts.Count == 1) parts.Add("Completed");
            return string.Join("   ·   ", parts);
        }
    }
}
