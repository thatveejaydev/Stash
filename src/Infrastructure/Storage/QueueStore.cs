using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Stash
{
    // Saves the queue between sessions. Unfinished items come back paused (with their partial
    // files) so nothing starts downloading unexpectedly; completed items keep their history.
    internal sealed class QueueStore
    {
        private const int CurrentVersion = 1;
        private const int MaximumCompletedItems = 200;
        private readonly string path;

        internal QueueStore(string path) { this.path = path; }

        internal sealed class QueueFile
        {
            public int Version { get; set; }
            public List<QueueRecord> Items { get; set; }
        }

        internal sealed class QueueRecord
        {
            public string Url { get; set; }
            public string Title { get; set; }
            public string SourceId { get; set; }
            public int Mode { get; set; }
            public int Resolution { get; set; }
            public bool EmbedMetadata { get; set; }
            public bool EmbedThumbnail { get; set; }
            public bool EmbedSubtitles { get; set; }
            public bool IncludeAutoCaptions { get; set; }
            public string TranscriptFormat { get; set; }
            public string TranscriptLanguage { get; set; }
            public string BrowserCookies { get; set; }
            public string FileNameStyle { get; set; }
            public int ConcurrentFragments { get; set; }
            public string BadgeLabel { get; set; }
            public string OutputFolder { get; set; }
            public string TemporaryFolder { get; set; }
            public string CustomBaseFileName { get; set; }
            public bool MetadataResolved { get; set; }
            public bool Completed { get; set; }
            public double ProgressPercent { get; set; }
            public string OutputFilePath { get; set; }
            public string Thumbnail { get; set; }
        }

        internal void Save(IList<DownloadQueueItem> items)
        {
            List<QueueRecord> records = new List<QueueRecord>();
            int completed = 0;
            foreach (DownloadQueueItem item in items)
            {
                bool done = item.Status == QueueItemStatus.Completed;
                // Cancelled items have no partial data left; finished history is bounded.
                if (item.Status == QueueItemStatus.Cancelled) continue;
                if (done && ++completed > MaximumCompletedItems) continue;
                records.Add(new QueueRecord
                {
                    Url = item.Url, Title = item.Title, SourceId = item.SourceId,
                    Mode = (int)item.Mode, Resolution = (int)item.Resolution,
                    EmbedMetadata = item.EmbedMetadata, EmbedThumbnail = item.EmbedThumbnail,
                    EmbedSubtitles = item.EmbedSubtitles, IncludeAutoCaptions = item.IncludeAutoCaptions,
                    TranscriptFormat = item.TranscriptFormat.ToString(), TranscriptLanguage = item.TranscriptLanguage,
                    BrowserCookies = item.BrowserCookies.ToString(), FileNameStyle = item.FileNameStyle.ToString(),
                    ConcurrentFragments = item.ConcurrentFragments, BadgeLabel = item.BadgeLabel,
                    OutputFolder = item.OutputFolder, TemporaryFolder = item.TemporaryFolder,
                    CustomBaseFileName = item.CustomBaseFileName, MetadataResolved = item.MetadataResolved,
                    Completed = done, ProgressPercent = item.ProgressPercent, OutputFilePath = item.OutputFilePath,
                    Thumbnail = item.ThumbnailUri == null ? null : item.ThumbnailUri.AbsoluteUri
                });
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            AtomicFile.WriteAllText(path, serializer.Serialize(new QueueFile { Version = CurrentVersion, Items = records }));
        }

        internal List<DownloadQueueItem> Load()
        {
            List<DownloadQueueItem> restored = new List<DownloadQueueItem>();
            if (!File.Exists(path)) return restored;
            QueueFile file;
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                file = serializer.Deserialize<QueueFile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                AppLog.Error("Saved queue could not be read", ex);
                return restored;
            }
            if (file == null || file.Items == null) return restored;
            foreach (QueueRecord record in file.Items)
            {
                DownloadQueueItem item = Restore(record);
                if (item != null) restored.Add(item);
            }
            return restored;
        }

        private static DownloadQueueItem Restore(QueueRecord record)
        {
            Uri url;
            if (record == null || string.IsNullOrWhiteSpace(record.OutputFolder) || !Path.IsPathRooted(record.OutputFolder) ||
                !Uri.TryCreate(record.Url, UriKind.Absolute, out url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps) ||
                !Enum.IsDefined(typeof(DownloadMode), record.Mode) || !Enum.IsDefined(typeof(VideoResolution), record.Resolution))
                return null;
            try
            {
                DownloadQueueItem item = new DownloadQueueItem(record.Url, (DownloadMode)record.Mode, (VideoResolution)record.Resolution,
                    record.EmbedMetadata, record.EmbedThumbnail, record.ConcurrentFragments, record.BadgeLabel ?? "", record.OutputFolder);
                item.Title = string.IsNullOrWhiteSpace(record.Title) ? record.Url : record.Title;
                item.SourceId = record.SourceId;
                item.EmbedSubtitles = record.EmbedSubtitles;
                item.IncludeAutoCaptions = record.IncludeAutoCaptions;
                TranscriptFormat transcriptFormat;
                if (Enum.TryParse(record.TranscriptFormat, true, out transcriptFormat) &&
                    Enum.IsDefined(typeof(TranscriptFormat), transcriptFormat)) item.TranscriptFormat = transcriptFormat;
                if (TranscriptCaptions.IsLanguageCode(record.TranscriptLanguage)) item.TranscriptLanguage = record.TranscriptLanguage;
                CookieBrowser browser;
                if (Enum.TryParse(record.BrowserCookies, true, out browser) && Enum.IsDefined(typeof(CookieBrowser), browser)) item.BrowserCookies = browser;
                FileNameStyle style;
                if (Enum.TryParse(record.FileNameStyle, true, out style) && Enum.IsDefined(typeof(FileNameStyle), style)) item.FileNameStyle = style;
                // Only reuse a temporary folder that belongs to this output folder.
                if (!string.IsNullOrWhiteSpace(record.TemporaryFolder) &&
                    StorageFileService.SameDirectory(Path.GetDirectoryName(Path.GetFullPath(record.TemporaryFolder)), record.OutputFolder) &&
                    Path.GetFileName(record.TemporaryFolder).StartsWith(StorageFileService.TemporaryFolderPrefix, StringComparison.Ordinal))
                    item.TemporaryFolder = Path.GetFullPath(record.TemporaryFolder);
                item.CustomBaseFileName = record.CustomBaseFileName;
                item.MetadataResolved = record.MetadataResolved;
                item.OutputFilePath = record.OutputFilePath;
                Uri thumbnail;
                if (Uri.TryCreate(record.Thumbnail, UriKind.Absolute, out thumbnail) &&
                    (thumbnail.Scheme == Uri.UriSchemeHttp || thumbnail.Scheme == Uri.UriSchemeHttps))
                    item.ThumbnailUri = thumbnail;
                if (record.Completed)
                {
                    item.Status = QueueItemStatus.Completed;
                    item.StatusText = "Completed";
                    item.ProgressPercent = 100;
                    item.Eta = "00:00";
                }
                else
                {
                    item.Status = QueueItemStatus.Paused;
                    item.StatusText = "Paused";
                    item.ProgressPercent = Math.Max(0, Math.Min(100, record.ProgressPercent));
                }
                return item;
            }
            catch (Exception ex)
            {
                AppLog.Error("A saved queue item could not be restored", ex);
                return null;
            }
        }
    }
}
