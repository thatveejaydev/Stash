using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Stash
{
    internal enum ShutdownMode
    {
        // Cancel running downloads and delete their partial files; idle items are left untouched.
        CancelRunning,
        // Pause running downloads and keep every partial file so the queue can resume next launch.
        PauseForResume,
        // Cancel everything and delete the partial files of every unfinished item.
        DiscardAll
    }

    // All queue state belongs to the creating (UI) thread. Process callbacks are
    // posted to that thread so painting, scheduling and user commands see one order.
    public sealed class DownloadManager
    {
        private const int MetadataTimeoutMilliseconds = 20000;

        private sealed class Execution
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public bool PauseRequested;
            public Task Completion;
        }

        private readonly List<DownloadQueueItem> items = new List<DownloadQueueItem>();
        private readonly Dictionary<DownloadQueueItem, Execution> running = new Dictionary<DownloadQueueItem, Execution>();
        private readonly List<Task> cleanups = new List<Task>();
        private readonly SynchronizationContext context;
        private readonly int ownerThread;
        private readonly IDownloadProcessRunner processRunner;
        private readonly Func<DownloadQueueItem, string, string, bool> confirmFileName;
        private string ytDlpPath;
        private string ffmpegPath;
        private int parallelCount = 1;
        private bool stopping;
        private bool scheduling;

        public event Action Changed;
        public event Action ProgressChanged;
        public event Action<string> Log;
        public event Action<string> ErrorLog;
        // Raised when a rename chosen during the download could not be applied; the file keeps its original name.
        internal event Action<DownloadQueueItem, string, Exception> PendingRenameFailed;

        public DownloadManager(IDownloadProcessRunner processRunner,
            Func<DownloadQueueItem, string, string, bool> confirmFileName)
        {
            context = SynchronizationContext.Current;
            if (context == null) throw new InvalidOperationException("A UI synchronization context is required.");
            ownerThread = Thread.CurrentThread.ManagedThreadId;
            this.processRunner = processRunner;
            this.confirmFileName = confirmFileName;
        }

        public List<DownloadQueueItem> GetItems()
        {
            VerifyThread();
            return new List<DownloadQueueItem>(items);
        }

        internal static bool IsActive(QueueItemStatus status)
        {
            return status == QueueItemStatus.FetchingInfo || status == QueueItemStatus.Downloading || status == QueueItemStatus.Merging;
        }

        internal static bool IsUnfinished(QueueItemStatus status)
        {
            return IsActive(status) || status == QueueItemStatus.Queued || status == QueueItemStatus.Paused;
        }

        public void AddItems(IList<DownloadQueueItem> newItems)
        {
            VerifyThread();
            if (stopping) return;
            items.InsertRange(0, newItems);
            NotifyChanged();
        }

        // Restored items keep their saved order and status; nothing is scheduled automatically.
        internal void AddRestoredItems(IList<DownloadQueueItem> restored)
        {
            VerifyThread();
            if (stopping) return;
            items.AddRange(restored);
            NotifyChanged();
        }

        public void ProcessQueue(string executable, string ffmpeg, int maximumParallel)
        {
            VerifyThread();
            ytDlpPath = executable;
            ffmpegPath = ffmpeg;
            parallelCount = Math.Max(1, Math.Min(6, maximumParallel));
            Schedule();
        }

        private void Schedule()
        {
            if (stopping || scheduling) return;
            scheduling = true;
            try
            {
                while (running.Count < parallelCount)
                {
                    DownloadQueueItem item = items.Find(x => x.Status == QueueItemStatus.Queued);
                    if (item == null) break;
                    Execution execution = new Execution();
                    running.Add(item, execution);
                    item.Status = QueueItemStatus.FetchingInfo;
                    item.StatusText = "Checking source";
                    execution.Completion = ExecuteAsync(item, execution, ytDlpPath, ffmpegPath);
                }
            }
            finally { scheduling = false; }
            NotifyChanged();
        }

        public void TogglePauseResume(DownloadQueueItem item)
        {
            VerifyThread();
            if (stopping || item == null || !items.Contains(item)) return;
            Execution execution;
            if (running.TryGetValue(item, out execution))
            {
                RequestPause(item, execution);
            }
            else if (item.Status == QueueItemStatus.Queued)
            {
                MarkPaused(item);
            }
            else if (item.Status == QueueItemStatus.Paused)
            {
                item.Status = QueueItemStatus.Queued;
                item.StatusText = "Resuming...";
                WriteLog(string.Format("[{0}] Resumed: {1}", item.Id, item.Title));
                Schedule();
            }
            NotifyChanged();
        }

        private static bool RequestPause(DownloadQueueItem item, Execution execution)
        {
            if (item.Status != QueueItemStatus.FetchingInfo && item.Status != QueueItemStatus.Downloading) return false;
            if (execution.Cancellation.IsCancellationRequested) return false;
            execution.PauseRequested = true;
            item.StatusText = "Pausing...";
            execution.Cancellation.Cancel();
            return true;
        }

        internal int PauseAll()
        {
            VerifyThread();
            if (stopping) return 0;
            int paused = 0;
            foreach (DownloadQueueItem item in new List<DownloadQueueItem>(items))
            {
                Execution execution;
                if (running.TryGetValue(item, out execution)) { if (RequestPause(item, execution)) paused++; }
                else if (item.Status == QueueItemStatus.Queued) { MarkPaused(item); paused++; }
            }
            NotifyChanged();
            return paused;
        }

        internal int ResumeAll()
        {
            VerifyThread();
            if (stopping) return 0;
            int resumed = 0;
            // Resume in display order so the top of the list starts first.
            foreach (DownloadQueueItem item in items)
            {
                if (item.Status != QueueItemStatus.Paused || running.ContainsKey(item)) continue;
                item.Status = QueueItemStatus.Queued;
                item.StatusText = "Resuming...";
                resumed++;
            }
            if (resumed > 0) Schedule();
            NotifyChanged();
            return resumed;
        }

        public void Cancel(DownloadQueueItem item)
        {
            VerifyThread();
            if (stopping || item == null || !items.Contains(item)) return;
            Execution execution;
            if (running.TryGetValue(item, out execution))
            {
                execution.PauseRequested = false;
                item.StatusText = "Cancelling...";
                execution.Cancellation.Cancel();
            }
            else if (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.Paused)
            {
                cleanups.RemoveAll(task => task.IsCompleted);
                cleanups.Add(FinishIdleCancellationAsync(item));
            }
            NotifyChanged();
        }

        internal static bool CanCancel(QueueItemStatus status)
        {
            return IsUnfinished(status);
        }

        public bool RemoveFinished(DownloadQueueItem item)
        {
            VerifyThread();
            if (stopping || item == null || !items.Contains(item)) return false;
            if (running.ContainsKey(item)) return false;
            if (item.Status != QueueItemStatus.Completed && item.Status != QueueItemStatus.Cancelled && item.Status != QueueItemStatus.Error) return false;
            if (item.Status == QueueItemStatus.Error)
            {
                cleanups.RemoveAll(task => task.IsCompleted);
                cleanups.Add(CleanupCancelledAsync(item));
            }
            return items.Remove(item);
        }

        public void Retry(DownloadQueueItem item)
        {
            Retry(item, null);
        }

        internal void Retry(DownloadQueueItem item, CookieBrowser? browserCookies)
        {
            VerifyThread();
            if (stopping || item == null || !items.Contains(item) || running.ContainsKey(item) ||
                (item.Status != QueueItemStatus.Error && item.Status != QueueItemStatus.Cancelled)) return;
            if (browserCookies.HasValue) item.BrowserCookies = browserCookies.Value;
            if (item.Status == QueueItemStatus.Cancelled)
            {
                item.ProgressPercent = 0;
                item.OutputFilePath = null;
                item.TrackedFilePaths.Clear();
            }
            item.Failure = null; item.ErrorOutput = "";
            item.Status = QueueItemStatus.Queued; item.StatusText = "Retrying...";
            item.Speed = "—"; item.Eta = "—";
            Schedule();
        }

        private async Task FinishIdleCancellationAsync(DownloadQueueItem item)
        {
            // Do not offer Retry until cleanup has finished using the temporary folder.
            item.Status = QueueItemStatus.Merging;
            item.StatusText = "Cancelling...";
            await CleanupCancelledAsync(item);
            MarkCancelled(item);
            NotifyChanged();
        }

        private void Fail(DownloadQueueItem item, string details)
        {
            string stage = item.Status == QueueItemStatus.Merging ? "Merging" : item.Status == QueueItemStatus.FetchingInfo ? "Fetching video information" : "Downloading";
            item.Failure = DownloadFailure.Create(stage, (item.ErrorOutput + "\n" + details).Trim());
            if (ErrorLog != null) ErrorLog(string.Format("[{0}] {1}\n{2}", item.Id, item.Title, item.Failure.TechnicalDetails));
            item.Status = QueueItemStatus.Error;
            item.StatusText = "Download failed";
            item.Speed = "—";
            item.Eta = "—";
        }

        public Task ShutdownAsync()
        {
            return ShutdownAsync(ShutdownMode.CancelRunning);
        }

        internal async Task ShutdownAsync(ShutdownMode mode)
        {
            VerifyThread();
            stopping = true;
            List<Task> pending = new List<Task>(cleanups);
            foreach (Execution execution in new List<Execution>(running.Values))
            {
                execution.PauseRequested = mode == ShutdownMode.PauseForResume;
                execution.Cancellation.Cancel();
                pending.Add(execution.Completion);
            }
            if (mode == ShutdownMode.DiscardAll)
            {
                foreach (DownloadQueueItem item in items)
                {
                    if (running.ContainsKey(item)) continue;
                    if (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.Paused || item.Status == QueueItemStatus.Error)
                        pending.Add(CleanupCancelledAsync(item));
                }
            }
            await Task.WhenAll(pending);
        }

        private async Task ExecuteAsync(DownloadQueueItem item, Execution execution, string executable, string ffmpeg)
        {
            CancellationToken token = execution.Cancellation.Token;
            Exception failure = null;
            try
            {
                try
                {
                    if (item.Mode == DownloadMode.TranscriptOnly)
                        await DownloadTranscriptAsync(item, executable, ffmpeg, token);
                    else
                        await DownloadAsync(item, execution, executable, ffmpeg, token);
                }
                catch (Exception ex) { failure = ex; }
                if (failure == null) return;
                if (!token.IsCancellationRequested)
                {
                    // Cleanup problems must never prevent the card from reaching a final state.
                    try { await StorageFileService.CleanupEmptyTemporaryFolderAsync(item); }
                    catch (Exception cleanupError) { ReportError(item, "Temporary folder cleanup failed", cleanupError); }
                }
                if (token.IsCancellationRequested)
                {
                    if (execution.PauseRequested) MarkPaused(item);
                    else
                    {
                        await CleanupCancelledAsync(item);
                        MarkCancelled(item);
                    }
                }
                else
                {
                    ReportError(item, "Download error", failure);
                    Fail(item, DescribeException(failure));
                    WriteLog(string.Format("[{0}] Error: {1}", item.Id, failure.Message));
                }
            }
            finally
            {
                running.Remove(item);
                execution.Cancellation.Dispose();
                NotifyChanged();
                Schedule();
            }
        }

        // User-facing details carry the message, never the stack trace (which can include local paths).
        private static string DescribeException(Exception exception)
        {
            Exception root = exception is AggregateException ? exception.GetBaseException() : exception;
            return root.Message;
        }

        private void ReportError(DownloadQueueItem item, string context, Exception exception)
        {
            if (ErrorLog != null) ErrorLog(string.Format("[{0}] {1}: {2}", item.Id, context, exception));
        }

        internal async Task<TranscriptCaptions.Source> InspectCaptionsAsync(string executable, string url, CookieBrowser browser, CancellationToken token)
        {
            string json = null;
            string errors = "";
            int exitCode;
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(MetadataTimeoutMilliseconds);
                try
                {
                    exitCode = await processRunner.RunAsync(executable,
                        YtDlpCommandBuilder.BuildTranscriptMetadataArguments(url, browser), timeout.Token,
                        line => json = line, line => errors = AppendEngineError(errors, line));
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Caption lookup timed out.");
                }
            }
            token.ThrowIfCancellationRequested();
            if (exitCode != 0 || string.IsNullOrWhiteSpace(json))
                throw new IOException("Caption lookup failed (engine exit code: " + exitCode + ").\n" + errors);

            TranscriptCaptions.Source source = await Task.Run(() => TranscriptCaptions.Parse(json), token);
            token.ThrowIfCancellationRequested();
            if (source == null) throw new InvalidDataException("The engine returned no video information.");
            return source;
        }

        private async Task DownloadTranscriptAsync(DownloadQueueItem item, string executable, string ffmpeg, CancellationToken token)
        {
            item.StatusText = "Finding captions";
            NotifyChanged();
            TranscriptCaptions.Source source = item.InspectedCaptions;
            item.InspectedCaptions = null;
            if (source == null) source = await InspectCaptionsAsync(executable, item.Url, item.BrowserCookies, token);
            token.ThrowIfCancellationRequested();
            item.Title = StorageFileService.SanitizeFileName(source.Title);
            if (string.IsNullOrEmpty(item.Title)) item.Title = item.Url;
            item.SourceId = source.Id ?? item.SourceId;
            item.Uploader = source.Uploader ?? source.Channel;
            TranscriptCaptions.Track track = TranscriptCaptions.Select(source, item.TranscriptLanguage);
            if (track == null)
            {
                item.ErrorOutput = "";
                Fail(item, "No captions available in this language.");
                return;
            }

            item.Status = QueueItemStatus.Downloading;
            item.StatusText = "Downloading captions";
            NotifyChanged();
            if (Directory.Exists(item.TemporaryFolder))
                await StorageFileService.CleanupPartialFilesForItemAsync(item);
            token.ThrowIfCancellationRequested();
            StorageFileService.CreateTemporaryFolder(item);
            if (Directory.GetFileSystemEntries(item.TemporaryFolder).Length > 0)
                throw new IOException("The temporary caption folder could not be cleared.");
            string errors = "";
            int exitCode = await processRunner.RunAsync(executable,
                YtDlpCommandBuilder.BuildTranscriptArguments(item, track, ffmpeg), token, null,
                line => errors = AppendEngineError(errors, line));
            item.ErrorOutput = errors;
            token.ThrowIfCancellationRequested();
            if (exitCode != 0)
            {
                Fail(item, "Caption download failed (engine exit code: " + exitCode + ").");
                return;
            }

            string baseName = YtDlpCommandBuilder.BuildBaseFileName(item.FileNameStyle, source.Title, item.SourceId, item.Uploader);
            if (string.IsNullOrEmpty(baseName)) baseName = "Transcript";
            baseName += " [" + TranscriptCaptions.WithoutOriginalSuffix(track.Language) + "]";
            string captionPath = StorageFileService.FindTranscriptCaption(item, YtDlpCommandBuilder.GetCaptionExtension(item.TranscriptFormat));
            string extension = YtDlpCommandBuilder.GetTranscriptExtension(item.TranscriptFormat);
            if (extension == ".txt")
            {
                item.Status = QueueItemStatus.Merging;
                item.StatusText = "Formatting transcript";
                NotifyChanged();
                string textPath = Path.Combine(item.TemporaryFolder, "transcript.txt");
                bool timed = item.TranscriptFormat == TranscriptFormat.TxtTimed;
                await Task.Run(() => TranscriptText.Write(captionPath, textPath, timed, track.Automatic, token), token);
                token.ThrowIfCancellationRequested();
                captionPath = textPath;
            }
            item.OutputFilePath = StorageFileService.PublishTranscript(item, captionPath, baseName, extension);
            ApplyPendingRename(item);
            Complete(item);
            NotifyChanged();
            try { await StorageFileService.CleanupPartialFilesForItemAsync(item); }
            catch (Exception error) { ReportError(item, "Temporary caption folder cleanup failed", error); }
        }

        private static string AppendEngineError(string errors, string line)
        {
            errors += line + "\n";
            return errors.Length > 16000 ? errors.Substring(errors.Length - 16000) : errors;
        }

        private async Task DownloadAsync(DownloadQueueItem item, Execution execution, string executable, string ffmpeg, CancellationToken token)
        {
            if (!item.MetadataResolved)
            {
                string[] metadata = await FetchMetadataAsync(executable, item, token);
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(metadata[0]))
                {
                    item.Title = metadata[0];
                    if (string.IsNullOrEmpty(item.SourceId)) item.SourceId = metadata[1];
                    item.Uploader = metadata[2];
                    if (!confirmFileName(item, metadata[0], metadata[1])) execution.Cancellation.Cancel();
                    else item.MetadataResolved = true;
                }
                else
                {
                    // Do not repeat a lookup that already failed on every resume; yt-dlp names the file itself.
                    item.MetadataResolved = true;
                }
            }
            else if (item.CustomBaseFileName == null && !string.IsNullOrWhiteSpace(item.Title) && item.Title != item.Url)
            {
                // Titles supplied up front (playlists) still go through duplicate and naming checks.
                if (!confirmFileName(item, item.Title, item.SourceId)) execution.Cancellation.Cancel();
            }
            // A pause/cancel can arrive while the rename dialog is open too.
            token.ThrowIfCancellationRequested();
            item.Status = QueueItemStatus.Downloading;
            item.StatusText = "Preparing files";
            NotifyChanged();
            StorageFileService.CreateTemporaryFolder(item);
            token.ThrowIfCancellationRequested();
            string arguments = YtDlpCommandBuilder.BuildArguments(item, ffmpeg);
            item.CompletedDetails = null;
            item.StatusText = "Checking source";
            NotifyChanged();
            WriteLog(string.Format("[{0}] yt-dlp {1}", item.Id, arguments));
            int exitCode;
            using (ProgressOutputBuffer output = new ProgressOutputBuffer((line, error) => PostOutput(item, execution, line, error)))
            {
                exitCode = await processRunner.RunAsync(executable, arguments, token,
                    line => output.Add(line, false), line => output.Add(line, true));
            }
            token.ThrowIfCancellationRequested();
            if (exitCode == 0)
            {
                // Let already-posted output finish before showing the final cleanup stage.
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                item.Status = QueueItemStatus.Merging;
                item.StatusText = "Finalizing output";
                item.Speed = "—";
                item.Eta = "—";
                NotifyChanged();
                try { await StorageFileService.CleanupPartialFilesForItemAsync(item); }
                catch (Exception cleanupError) { ReportError(item, "Temporary file cleanup failed", cleanupError); }
                token.ThrowIfCancellationRequested();
                item.CompletedDetails = await CompletedDownloadDetails.ReadFileAsync(item.OutputFilePath, item.CompletedDetails);
                token.ThrowIfCancellationRequested();
                ApplyPendingRename(item);
                Complete(item);
            }
            else
            {
                try { await StorageFileService.CleanupEmptyTemporaryFolderAsync(item); }
                catch (Exception cleanupError) { ReportError(item, "Temporary folder cleanup failed", cleanupError); }
                token.ThrowIfCancellationRequested();
                Fail(item, "Engine exit code: " + exitCode);
                WriteLog(string.Format("[{0}] Failed with exit code {1}", item.Id, exitCode));
            }
        }

        // Runs once the engine has released the finished file, so the active download is never disturbed.
        // A failed rename keeps the original name and never fails the download.
        private void ApplyPendingRename(DownloadQueueItem item)
        {
            string newBaseName = item.PendingRenameBaseName;
            item.PendingRenameBaseName = null;
            if (string.IsNullOrEmpty(newBaseName)) return;
            try
            {
                string path = item.OutputFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new FileNotFoundException("The finished file could not be found.", path);
                item.OutputFilePath = StorageFileService.RenameDownloadedFile(path, newBaseName);
                item.Title = newBaseName;
                WriteLog(string.Format("[{0}] Renamed on completion: {1}", item.Id, Path.GetFileName(item.OutputFilePath)));
            }
            catch (Exception ex)
            {
                ReportError(item, "Rename on completion failed", ex);
                if (PendingRenameFailed != null && !stopping) PendingRenameFailed(item, newBaseName, ex);
            }
        }

        private void Complete(DownloadQueueItem item)
        {
            item.ProgressPercent = 100;
            item.Speed = "—";
            item.Eta = "00:00";
            item.Status = QueueItemStatus.Completed;
            item.StatusText = "Completed";
            WriteLog(string.Format("[{0}] Completed: {1}", item.Id, item.Title));
        }

        // Returns { title, id, uploader }; empty strings when the lookup fails.
        private async Task<string[]> FetchMetadataAsync(string executable, DownloadQueueItem item, CancellationToken token)
        {
            List<string> lines = new List<string>();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(MetadataTimeoutMilliseconds);
                try
                {
                    await processRunner.RunAsync(executable, YtDlpCommandBuilder.BuildMetadataArguments(item),
                        timeout.Token, line => { if (!string.IsNullOrWhiteSpace(line)) lock (lines) lines.Add(line); }, null);
                    string[] result;
                    lock (lines)
                    {
                        result = new[]
                        {
                            lines.Count > 0 ? StorageFileService.SanitizeFileName(lines[0].Trim()) : "",
                            lines.Count > 1 ? lines[1].Trim() : "",
                            lines.Count > 2 ? lines[2].Trim() : ""
                        };
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    WriteLog("Metadata lookup failed: " + ex.Message);
                    return new[] { "", "", "" };
                }
            }
        }

        private void PostOutput(DownloadQueueItem item, Execution execution, string line, bool isError)
        {
            context.Post(_ =>
            {
                Execution current;
                // Ignore delayed output from an interrupted or already finished run.
                if (stopping || !running.TryGetValue(item, out current) || current != execution || execution.Cancellation.IsCancellationRequested) return;
                if (isError && !line.StartsWith("STASH_PROGRESS:", StringComparison.Ordinal))
                {
                    item.ErrorOutput += line + "\n";
                    if (item.ErrorOutput.Length > 16000) item.ErrorOutput = item.ErrorOutput.Substring(item.ErrorOutput.Length - 16000);
                    WriteLog(string.Format("[{0} ERR] {1}", item.Id, line));
                }
                else
                {
                    QueueItemStatus previousStatus = item.Status;
                    string previousTitle = item.Title;
                    bool updated = false;
                    YtDlpOutputParser.ParseLine(item, line, () => updated = true, WriteLog);
                    if (item.Status != previousStatus || item.Title != previousTitle) NotifyChanged();
                    else if (updated && ProgressChanged != null) ProgressChanged();
                }
            }, null);
        }

        private void MarkPaused(DownloadQueueItem item)
        {
            item.Status = QueueItemStatus.Paused;
            item.StatusText = "Paused";
            item.Speed = "—";
            item.Eta = "—";
            WriteLog(string.Format("[{0}] Paused at {1:0.0}%: {2}", item.Id, item.ProgressPercent, item.Title));
        }

        private void MarkCancelled(DownloadQueueItem item)
        {
            item.Status = QueueItemStatus.Cancelled;
            item.StatusText = "Cancelled";
            item.Speed = "—";
            item.Eta = "—";
            WriteLog(string.Format("[{0}] Cancelled: {1}", item.Id, item.Title));
        }

        private async Task CleanupCancelledAsync(DownloadQueueItem item)
        {
            try
            {
                int deleted = await StorageFileService.CleanupPartialFilesForItemAsync(item);
                if (deleted > 0) WriteLog(string.Format("[{0}] Cleaned up {1} temporary fragment file(s).", item.Id, deleted));
            }
            catch (Exception ex)
            {
                if (ErrorLog != null) ErrorLog(string.Format("[{0}] Cleanup failed: {1}", item.Id, ex));
            }
        }

        private void VerifyThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Download commands must run on the owning UI thread.");
        }

        private void NotifyChanged() { if (!stopping && Changed != null) Changed(); }
        private void WriteLog(string message) { if (!stopping && Log != null) Log(message); }
    }
}
