using System;
using System.Collections.Generic;

namespace MediaDownloader
{
    public class DownloadQueueItem
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DownloadMode Mode { get; set; }
        public VideoResolution Resolution { get; set; }
        public bool EmbedMetadata { get; set; }
        public bool EmbedThumbnail { get; set; }
        public bool EmbedSubtitles { get; set; }
        public bool IncludeAutoCaptions { get; set; }
        public int ConcurrentFragments { get; set; }
        public string BadgeLabel { get; set; }
        public string OutputFolder { get; set; }
        public string TemporaryFolder { get; private set; }
        public string CustomBaseFileName { get; set; }
        public QueueItemStatus Status { get; internal set; }
        public string StatusText { get; internal set; }
        public double ProgressPercent { get; internal set; }
        public string Speed { get; internal set; }
        public string Eta { get; internal set; }
        public string OutputFilePath { get; internal set; }
        public bool MetadataResolved { get; set; }
        public List<string> TrackedFilePaths { get; set; }

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
            TemporaryFolder = System.IO.Path.Combine(System.IO.Path.GetFullPath(outputFolder), ".stash-temp-" + Guid.NewGuid().ToString("N"));
            CustomBaseFileName = null;
            Status = QueueItemStatus.Queued;
            StatusText = "Queued";
            ProgressPercent = 0.0;
            Speed = "—";
            Eta = "—";
            MetadataResolved = false;
            TrackedFilePaths = new List<string>();
        }
    }
}
