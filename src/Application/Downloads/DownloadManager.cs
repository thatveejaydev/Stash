using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MediaDownloader
{
    // All queue state belongs to the creating (UI) thread. Process callbacks are
    // posted to that thread so painting, scheduling and user commands see one order.
    public sealed class DownloadManager
    {
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

        public void AddItems(IList<DownloadQueueItem> newItems)
        {
            VerifyThread();
            if (stopping) return;
            items.InsertRange(0, newItems);
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
                    item.StatusText = "Fetching info...";
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
                if (item.Status != QueueItemStatus.FetchingInfo && item.Status != QueueItemStatus.Downloading) return;
                if (execution.Cancellation.IsCancellationRequested) return;
                execution.PauseRequested = true;
                item.StatusText = "Pausing...";
                execution.Cancellation.Cancel();
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

        public bool RemoveFinished(DownloadQueueItem item)
        {
            VerifyThread();
            if (running.ContainsKey(item)) return false;
            if (item.Status != QueueItemStatus.Completed && item.Status != QueueItemStatus.Cancelled && item.Status != QueueItemStatus.Error) return false;
            return items.Remove(item);
        }

        public void Retry(DownloadQueueItem item)
        {
            VerifyThread();
            if (stopping || item == null || !items.Contains(item) || running.ContainsKey(item) ||
                (item.Status != QueueItemStatus.Error && item.Status != QueueItemStatus.Cancelled)) return;
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
            item.Failure = DownloadFailure.Create(item.Status == QueueItemStatus.Merging ? "Merging" : item.Status == QueueItemStatus.FetchingInfo ? "Fetching video information" : "Downloading", item.ErrorOutput + "\n" + details);
            if (ErrorLog != null) ErrorLog(string.Format("[{0}] {1}\n{2}", item.Id, item.Title, item.Failure.TechnicalDetails));
            item.Status = QueueItemStatus.Error;
            item.StatusText = "Download failed";
        }

        public async Task ShutdownAsync()
        {
            VerifyThread();
            stopping = true;
            List<Task> pending = new List<Task>(cleanups);
            foreach (Execution execution in new List<Execution>(running.Values))
            {
                execution.PauseRequested = false;
                execution.Cancellation.Cancel();
                pending.Add(execution.Completion);
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
                    await DownloadAsync(item, execution, executable, ffmpeg, token);
                }
                catch (Exception ex) { failure = ex; }
                if (failure != null && token.IsCancellationRequested)
                {
                    if (execution.PauseRequested) MarkPaused(item);
                    else
                    {
                        await CleanupCancelledAsync(item);
                        MarkCancelled(item);
                    }
                }
                else if (failure != null)
                {
                    Fail(item, failure.ToString());
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

        private async Task DownloadAsync(DownloadQueueItem item, Execution execution, string executable, string ffmpeg, CancellationToken token)
        {
            if (!item.MetadataResolved)
            {
                Tuple<string, string> metadata = await FetchMetadataAsync(executable, item.Url, token);
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(metadata.Item1))
                {
                    item.Title = metadata.Item1;
                    if (!confirmFileName(item, metadata.Item1, metadata.Item2))
                    {
                        execution.Cancellation.Cancel();
                    }
                    else item.MetadataResolved = true;
                }
            }
            // A pause/cancel can arrive while the rename dialog is open too.
            token.ThrowIfCancellationRequested();
            item.Status = QueueItemStatus.Downloading;
            item.StatusText = item.ProgressPercent > 0 ? "Resuming..." : "Starting...";
            NotifyChanged();
            Directory.CreateDirectory(item.TemporaryFolder);
            string arguments = YtDlpCommandBuilder.BuildArguments(item, ffmpeg);
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
                await StorageFileService.CleanupPartialFilesForItemAsync(item);
                token.ThrowIfCancellationRequested();
                item.ProgressPercent = 100;
                item.Speed = "—";
                item.Eta = "00:00";
                item.Status = QueueItemStatus.Completed;
                item.StatusText = "Completed";
                WriteLog(string.Format("[{0}] Completed: {1}", item.Id, item.Title));
            }
            else
            {
                Fail(item, "Engine exit code: " + exitCode);
                WriteLog(string.Format("[{0}] Failed with exit code {1}", item.Id, exitCode));
            }
        }

        private async Task<Tuple<string, string>> FetchMetadataAsync(string executable, string url, CancellationToken token)
        {
            List<string> lines = new List<string>();
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(9000);
                try
                {
                    await processRunner.RunAsync(executable,
                        string.Format("--no-playlist --get-title --get-id --no-warnings \"{0}\"", url.Replace("\"", "")),
                        timeout.Token, line => { if (!string.IsNullOrWhiteSpace(line)) lines.Add(line); }, null);
                    string title = lines.Count > 0 ? StorageFileService.SanitizeFileName(lines[0].Trim()) : "";
                    string id = lines.Count > 1 ? lines[lines.Count - 1].Trim() : "";
                    return Tuple.Create(title, id);
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    WriteLog("Metadata lookup failed: " + ex.Message);
                    return Tuple.Create("", "");
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
