using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Stash;

internal static class DownloadManagerTests
{
    private sealed class UiContext : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new Queue<Action>();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        public override void Post(SendOrPostCallback callback, object state)
        {
            lock (callbacks) callbacks.Enqueue(() => callback(state));
            ready.Set();
        }
        public void Pump()
        {
            Action callback = null;
            lock (callbacks) { if (callbacks.Count > 0) callback = callbacks.Dequeue(); }
            if (callback != null) callback();
            else ready.WaitOne(10);
        }
    }

    private sealed class Call
    {
        public string Arguments;
        public CancellationToken Token;
        public Action<string> Output;
        public readonly TaskCompletionSource<int> Result = new TaskCompletionSource<int>();
    }

    private sealed class FakeRunner : IDownloadProcessRunner
    {
        public readonly List<Call> Calls = new List<Call>();
        public bool FailStart;
        public Task<int> RunAsync(string executable, string arguments, CancellationToken token,
            Action<string> output, Action<string> error)
        {
            if (FailStart) throw new InvalidOperationException("Simulated startup failure");
            Call call = new Call { Arguments = arguments, Token = token, Output = output };
            Calls.Add(call);
            // Tests decide when the process actually exits, independently of cancellation.
            return call.Result.Task;
        }
    }

    private static int assertions;
    private static string testFolder;

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Exception("Timed out waiting for state transition");
            await Task.Delay(10);
        }
    }

    private static DownloadQueueItem Item(bool metadataResolved = true)
    {
        DownloadQueueItem item = new DownloadQueueItem("https://example.com/video", DownloadMode.BestVideoAndAudioMp4,
            VideoResolution.Best, false, false, 1, "MP4", testFolder);
        item.MetadataResolved = metadataResolved;
        return item;
    }

    private static DownloadManager Manager(FakeRunner runner)
    {
        return new DownloadManager(runner, (item, title, id) => true);
    }

    private static async Task QueueAndParallelism()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        int stateNotifications = 0, progressNotifications = 0;
        manager.Changed += () => stateNotifications++;
        manager.ProgressChanged += () => progressNotifications++;
        DownloadQueueItem first = Item(), second = Item(), third = Item();
        manager.AddItems(new[] { first, second, third });
        manager.ProcessQueue("fake", null, 2);
        Assert(runner.Calls.Count == 2 && third.Status == QueueItemStatus.Queued, "Parallel limit must hold");
        manager.TogglePauseResume(third);
        Assert(third.Status == QueueItemStatus.Paused, "Queued item can pause without starting a process");
        int beforeProgress = stateNotifications;
        runner.Calls[0].Output("[download] 25.0% of 10MiB at 1MiB/s ETA 00:10");
        await Until(() => first.ProgressPercent == 25);
        Assert(first.Speed == "8.4 Mbps", "Output should be parsed on the owner thread");
        Assert(stateNotifications == beforeProgress && progressNotifications == 1, "Progress must use the throttled notification path");
        string completedFile = Path.Combine(testFolder, "completed-media.mp4");
        File.WriteAllBytes(completedFile, new byte[1452]);
        runner.Calls[0].Output("STASH_FINAL:" + completedFile);
        runner.Calls[0].Output("STASH_MEDIA:304|3840|2160|25|mp4");
        runner.Calls[0].Result.SetResult(0);
        await Until(() => first.Status == QueueItemStatus.Completed);
        Assert(first.CompletedDetails != null && first.CompletedDetails.FileSizeBytes == 1452,
            "Completed download must retain final metadata and read actual output size");
        Assert(first.CompletedDetails.GetSummary(first.Mode) == "05:04   ·   1.5 KB   ·   MP4   ·   3840×2160 (4K)   ·   25 fps",
            "Completed video summary must include duration, size, final format, actual resolution and frame rate");
        Assert(runner.Calls[0].Arguments.Contains("after_move:STASH_MEDIA:"), "Download command must request final selected media metadata");
        Assert(runner.Calls.Count == 2, "Paused queued item must not start after another finishes");
        manager.TogglePauseResume(third);
        Assert(runner.Calls.Count == 3, "Resume should fill the available slot");
        runner.Calls[1].Result.SetResult(7);
        await Until(() => second.Status == QueueItemStatus.Error);
        Assert(manager.RemoveFinished(second), "Failed item should be removable");
        runner.Calls[2].Result.SetResult(0);
        await Until(() => third.Status == QueueItemStatus.Completed);
        await manager.ShutdownAsync();
    }

    private static async Task PendingRenameOnCompletion()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        List<string> failures = new List<string>();
        manager.PendingRenameFailed += (failed, name, error) => failures.Add(name);
        DownloadQueueItem renamed = Item(), blocked = Item();
        manager.AddItems(new[] { renamed, blocked });
        manager.ProcessQueue("fake", null, 2);
        await Until(() => runner.Calls.Count == 2);

        renamed.PendingRenameBaseName = "My new name";
        Assert(renamed.DisplayTitle == "My new name", "A pending rename must be shown while downloading");
        string original = Path.Combine(testFolder, "pending-original.mp4");
        File.WriteAllBytes(original, new byte[16]);
        runner.Calls[0].Output("[download] 40.0% of 10MiB at 1MiB/s ETA 00:10");
        await Until(() => renamed.ProgressPercent == 40);
        Assert(renamed.Status == QueueItemStatus.Downloading && runner.Calls[0].Token.IsCancellationRequested == false,
            "A pending rename must not interrupt the running download");
        runner.Calls[0].Output("STASH_FINAL:" + original);
        runner.Calls[0].Result.SetResult(0);
        await Until(() => renamed.Status == QueueItemStatus.Completed);
        string expected = Path.Combine(testFolder, "My new name.mp4");
        Assert(renamed.OutputFilePath == expected && File.Exists(expected) && !File.Exists(original),
            "The finished file must be renamed on completion");
        Assert(renamed.Title == "My new name" && renamed.PendingRenameBaseName == null, "The rename must be applied once");

        // A name taken while downloading keeps the original file and reports the failure without failing the download.
        string taken = Path.Combine(testFolder, "Taken name.mp4");
        File.WriteAllBytes(taken, new byte[4]);
        blocked.PendingRenameBaseName = "Taken name";
        string blockedFile = Path.Combine(testFolder, "pending-blocked.mp4");
        File.WriteAllBytes(blockedFile, new byte[8]);
        runner.Calls[1].Output("STASH_FINAL:" + blockedFile);
        runner.Calls[1].Result.SetResult(0);
        await Until(() => blocked.Status == QueueItemStatus.Completed);
        Assert(blocked.OutputFilePath == blockedFile && File.Exists(blockedFile) && new FileInfo(taken).Length == 4,
            "A failed rename must keep the original file and never overwrite another");
        Assert(failures.Count == 1 && failures[0] == "Taken name" && blocked.PendingRenameBaseName == null,
            "A failed rename must be reported once");
        await manager.ShutdownAsync();

        QueueStore store = new QueueStore(Path.Combine(testFolder, "pending-queue.json"));
        DownloadQueueItem paused = Item();
        paused.PendingRenameBaseName = "Saved rename";
        DownloadQueueItem invalid = Item();
        invalid.PendingRenameBaseName = "bad:name";
        store.Save(new[] { paused, invalid });
        List<DownloadQueueItem> restored = store.Load();
        Assert(restored.Count == 2 && restored[0].PendingRenameBaseName == "Saved rename" && restored[1].PendingRenameBaseName == null,
            "Pending renames must survive a restart and invalid names must be dropped");
    }

    private static async Task PauseResumeAndStaleOutput()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem item = Item();
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        string partial = Path.Combine(item.TemporaryFolder, "video.part");
        File.WriteAllText(partial, "partial");
        manager.TogglePauseResume(item);
        manager.TogglePauseResume(item);
        Assert(runner.Calls[0].Token.IsCancellationRequested, "Pause must request process cancellation");
        Assert(item.Status != QueueItemStatus.Paused && runner.Calls.Count == 1, "Resume must wait for the old process to exit");
        runner.Calls[0].Result.SetCanceled();
        await Until(() => item.Status == QueueItemStatus.Paused);
        Assert(File.Exists(partial), "Pause must preserve partial files");
        manager.TogglePauseResume(item);
        Assert(runner.Calls.Count == 2 && !runner.Calls[1].Token.IsCancellationRequested, "Resume needs a new execution token");
        runner.Calls[0].Output("[download] 95% of 10MiB at 1MiB/s ETA 00:01");
        await Task.Delay(30);
        Assert(item.ProgressPercent == 0, "Old output must not overwrite the resumed run");
        manager.Cancel(item);
        runner.Calls[1].Result.SetCanceled();
        await Until(() => item.Status == QueueItemStatus.Cancelled);
        Assert(!Directory.Exists(item.TemporaryFolder), "Cancel must clean up only after exit");
        await manager.ShutdownAsync();
    }

    private static async Task CancelOverridesPause()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem item = Item();
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        manager.TogglePauseResume(item);
        manager.Cancel(item);
        runner.Calls[0].Result.SetCanceled();
        await Until(() => item.Status == QueueItemStatus.Cancelled);
        manager.TogglePauseResume(item);
        Assert(runner.Calls.Count == 1, "Cancelled item must not resume");
        item.ProgressPercent = 34;
        manager.Retry(item);
        Assert(runner.Calls.Count == 2 && item.ProgressPercent == 0, "Retrying a cancelled download must start a fresh execution");
        runner.Calls[1].Result.SetResult(0);
        await Until(() => item.Status == QueueItemStatus.Completed);
        await manager.ShutdownAsync();
    }

    private static async Task MetadataCancellationAndRename()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem item = Item(false);
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        Assert(runner.Calls[0].Arguments.Contains("--print \"%(title)s\"") && runner.Calls[0].Arguments.Contains("--skip-download"), "Metadata must use the managed process runner");
        manager.Cancel(item);
        Assert(runner.Calls[0].Token.IsCancellationRequested, "Cancel must also stop metadata lookup");
        runner.Calls[0].Result.SetCanceled();
        await Until(() => item.Status == QueueItemStatus.Cancelled);
        Assert(runner.Calls.Count == 1, "No download may start after metadata cancellation");
        await manager.ShutdownAsync();

        runner = new FakeRunner();
        manager = null;
        int prompts = 0;
        manager = new DownloadManager(runner, (current, title, id) =>
        {
            prompts++;
            manager.Cancel(current); // Simulates cancellation while the UI prompt is open.
            return true;
        });
        item = Item(false);
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        runner.Calls[0].Output("Video title");
        runner.Calls[0].Output("video-id");
        runner.Calls[0].Result.SetResult(0);
        await Until(() => item.Status == QueueItemStatus.Cancelled);
        Assert(prompts == 1 && runner.Calls.Count == 1, "Cancellation in the prompt must prevent process startup");
        await manager.ShutdownAsync();
    }

    private static async Task BulkPauseAndRestartableShutdown()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem first = Item(), second = Item(), third = Item();
        manager.AddItems(new[] { first, second, third });
        manager.ProcessQueue("fake", null, 1);
        Assert(manager.PauseAll() == 3 && runner.Calls[0].Token.IsCancellationRequested, "Pause all stops the running item and holds queued ones");
        runner.Calls[0].Result.SetCanceled();
        await Until(() => first.Status == QueueItemStatus.Paused);
        Assert(second.Status == QueueItemStatus.Paused && third.Status == QueueItemStatus.Paused && runner.Calls.Count == 1,
            "Nothing new starts while everything is paused");
        Assert(manager.ResumeAll() == 3 && runner.Calls.Count == 2, "Resume all respects the parallel limit");

        // Closing with "Pause & Quit" keeps partial files for the next session.
        string fragment = Path.Combine(first.TemporaryFolder, "keep.part");
        File.WriteAllText(fragment, "partial");
        Assert((File.GetAttributes(first.TemporaryFolder) & FileAttributes.Hidden) != 0, "Temporary folders are hidden");
        Task shutdown = manager.ShutdownAsync(ShutdownMode.PauseForResume);
        runner.Calls[1].Result.SetCanceled();
        await shutdown;
        Assert(first.Status == QueueItemStatus.Paused && File.Exists(fragment), "Restartable shutdown pauses and keeps partial data");

        // "Cancel Downloads" removes partial files of idle items too.
        runner = new FakeRunner();
        manager = Manager(runner);
        DownloadQueueItem idle = Item();
        manager.AddRestoredItems(new[] { idle });
        manager.TogglePauseResume(idle);
        Assert(idle.Status == QueueItemStatus.Paused, "Restored items can be paused or resumed");
        Directory.CreateDirectory(idle.TemporaryFolder);
        File.WriteAllText(Path.Combine(idle.TemporaryFolder, "old.part"), "x");
        await manager.ShutdownAsync(ShutdownMode.DiscardAll);
        Assert(!Directory.Exists(idle.TemporaryFolder), "Discarding on exit deletes idle partial files");

        // Titles known up front still go through the naming and duplicate check exactly once.
        runner = new FakeRunner();
        int prompts = 0;
        manager = new DownloadManager(runner, (current, title, id) => { prompts++; current.CustomBaseFileName = title; return true; });
        DownloadQueueItem playlistItem = Item();
        playlistItem.Title = "Playlist entry";
        playlistItem.SourceId = "abc123";
        manager.AddItems(new[] { playlistItem });
        manager.ProcessQueue("fake", null, 1);
        Assert(prompts == 1 && runner.Calls.Count == 1 && runner.Calls[0].Arguments.Contains("Playlist entry.%(ext)s"),
            "Pre-resolved titles are named through the confirmation callback");
        runner.Calls[0].Result.SetResult(1);
        await Until(() => playlistItem.Status == QueueItemStatus.Error);
        Assert(!playlistItem.Failure.TechnicalDetails.Contains("   at "), "User-facing details never include stack traces");
        manager.Retry(playlistItem);
        Assert(prompts == 1, "Retry does not ask for the name again");
        runner.Calls[1].Result.SetResult(0);
        await Until(() => playlistItem.Status == QueueItemStatus.Completed);

        // A failed metadata lookup is not repeated on every resume.
        DownloadQueueItem unresolved = Item(false);
        manager.AddItems(new[] { unresolved });
        manager.ProcessQueue("fake", null, 1);
        runner.Calls[2].Result.SetResult(1);
        await Until(() => runner.Calls.Count == 4);
        Assert(unresolved.MetadataResolved && !runner.Calls[3].Arguments.Contains("--skip-download"), "Empty metadata falls through to the download");
        runner.Calls[3].Result.SetResult(0);
        await Until(() => unresolved.Status == QueueItemStatus.Completed);
        await manager.ShutdownAsync();
    }

    private static async Task ShutdownAndStartupFailure()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem first = Item(), second = Item();
        manager.AddItems(new[] { first, second });
        manager.ProcessQueue("fake", null, 1);
        Task shutdown = manager.ShutdownAsync();
        Assert(!shutdown.IsCompleted && runner.Calls[0].Token.IsCancellationRequested, "Shutdown must wait for process exit");
        runner.Calls[0].Result.SetCanceled();
        await shutdown;
        Assert(runner.Calls.Count == 1 && second.Status == QueueItemStatus.Queued, "Shutdown must not start queued work");

        runner = new FakeRunner { FailStart = true };
        manager = Manager(runner);
        first = Item(); second = Item();
        manager.AddItems(new[] { first, second });
        manager.ProcessQueue("missing.exe", null, 1);
        await Until(() => first.Status == QueueItemStatus.Error && second.Status == QueueItemStatus.Error);
        Assert(first.Status == QueueItemStatus.Error && second.Status == QueueItemStatus.Error, "Startup failures must release queue slots");
        await manager.ShutdownAsync();
    }

    private static async Task RealProcessOwnership()
    {
        DownloadProcessRunner runner = new DownloadProcessRunner();
        string executable = Assembly.GetExecutingAssembly().Location;
        int outputs = 0, errors = 0;
        Task<int> outputTask = runner.RunAsync(executable, "--child exit", CancellationToken.None,
            line => Interlocked.Increment(ref outputs), line => Interlocked.Increment(ref errors));
        await Task.WhenAny(outputTask, Task.Delay(4000));
        Assert(outputTask.IsCompleted, "Real process did not finish; output lines=" + outputs + ", error lines=" + errors);
        int exitCode = await outputTask;
        Assert(exitCode == 0 && outputs == 1 && errors == 1, "Real runner must drain both output streams");
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            outputs = 0;
            Task<int> task = runner.RunAsync(executable, "--child wait", cancellation.Token,
                line => Interlocked.Increment(ref outputs), null);
            await Until(() => Volatile.Read(ref outputs) > 0);
            cancellation.Cancel();
            bool cancelled = false;
            try { await task; } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation must terminate and await the real child process");
        }
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            bool cancelled = false;
            try { await runner.RunAsync("does-not-exist.exe", "", cancellation.Token, null, null); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation before startup must not launch a process");
        }
    }

    private static async Task RunAll()
    {
        await FailureFolderCleanup();
        FakeRunner retryRunner = new FakeRunner();
        DownloadManager retryManager = Manager(retryRunner);
        DownloadQueueItem retryItem = Item();
        retryManager.AddItems(new[] { retryItem });
        retryManager.ProcessQueue("fake", null, 1);
        retryRunner.Calls[0].Result.SetResult(1);
        await Until(() => retryItem.Status == QueueItemStatus.Error);
        Assert(retryItem.Failure != null && retryItem.Failure.TechnicalDetails.Contains("exit code: 1"), "Unknown failures must preserve exit details");
        retryManager.Retry(retryItem);
        Assert(retryRunner.Calls.Count == 2 && retryItem.Failure == null, "Retry must clear old failure and launch a new execution");
        retryRunner.Calls[1].Result.SetResult(0);
        await Until(() => retryItem.Status == QueueItemStatus.Completed);
        await retryManager.ShutdownAsync();
        Console.WriteLine("Queue and parallelism");
        await QueueAndParallelism();
        Console.WriteLine("Rename while downloading");
        await PendingRenameOnCompletion();
        Console.WriteLine("Pause/resume and stale output");
        await PauseResumeAndStaleOutput();
        Console.WriteLine("Cancel overrides pause");
        await CancelOverridesPause();
        Console.WriteLine("Metadata cancellation and rename");
        await MetadataCancellationAndRename();
        Console.WriteLine("Shutdown and startup failure");
        await ShutdownAndStartupFailure();
        Console.WriteLine("Bulk pause, resume and restartable shutdown");
        await BulkPauseAndRestartableShutdown();
        Console.WriteLine("Transcript-only downloads");
        await TranscriptOnlyDownloads();
        Console.WriteLine("Real process ownership");
        await RealProcessOwnership();
    }

    private static async Task TranscriptOnlyDownloads()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        string folder = Path.Combine(testFolder, "transcripts");
        Directory.CreateDirectory(folder);
        DownloadQueueItem item = new DownloadQueueItem("https://example.com/captions", DownloadMode.TranscriptOnly,
            VideoResolution.Best, true, true, 8, "Transcript", folder);
        string source = "{\"title\":\"Lesson\",\"id\":\"lesson\",\"subtitles\":{\"en\":[{\"ext\":\"vtt\"}]}," +
            "\"automatic_captions\":{\"en-orig\":[{\"ext\":\"vtt\"}],\"es\":[{\"ext\":\"vtt\"}]}}";
        Assert(TranscriptCaptions.Select(TranscriptCaptions.Parse(source), "es").Automatic, "Explicit languages can use auto captions");
        Assert(TranscriptCaptions.Select(TranscriptCaptions.Parse(
            "{\"automatic_captions\":{\"en-orig\":[{}],\"es\":[{}]}}"), null).Language == "en-orig",
            "Original must select the native auto-caption track");
        List<TranscriptCaptions.Track> available = TranscriptCaptions.ListTracks(TranscriptCaptions.Parse(source));
        Assert(available.Count == 2 && available[0].Language == "en" && !available[0].Automatic &&
            available[1].Language == "es" && available[1].Automatic,
            "Caption choices include available languages and prefer creator tracks of the same language");
        Assert(TranscriptCaptions.Select(TranscriptCaptions.Parse("{\"language\":\"en-US\",\"subtitles\":{\"en\":[{}]}}"), null).Language == "en",
            "Original can use the parent-language track when the source language is regional");
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        Assert(runner.Calls[0].Arguments.Contains("--dump-single-json"), "Transcript must inspect available caption tracks first");
        runner.Calls[0].Output(source);
        runner.Calls[0].Result.SetResult(0);
        await Until(() => runner.Calls.Count == 2 || item.Status == QueueItemStatus.Error);
        Assert(runner.Calls.Count == 2, "A caption track should be selected and downloaded");
        string arguments = runner.Calls[1].Arguments;
        Assert(arguments.Contains("--skip-download") && arguments.Contains("--write-subs") &&
            !arguments.Contains("--write-auto-subs") && arguments.Contains("--sub-langs \"^en$\"") &&
            arguments.Contains("--convert-subs srt") && !arguments.Contains("--embed"),
            "Original prefers edited captions and skips all audio, video, and embedding");
        string existing = Path.Combine(folder, "Lesson [en].srt");
        File.WriteAllText(existing, "existing");
        string srt = "1\n00:00:01,000 --> 00:00:02,000\nA timed caption.\n";
        File.WriteAllText(Path.Combine(item.TemporaryFolder, "caption.en.srt"), srt);
        runner.Calls[1].Result.SetResult(0);
        await Until(() => item.Status == QueueItemStatus.Completed);
        Assert(Path.GetFileName(item.OutputFilePath) == "Lesson [en] (2).srt" &&
            File.ReadAllText(item.OutputFilePath) == srt && File.ReadAllText(existing) == "existing",
            "Completing a transcript publishes the timed output without overwriting an existing file");

        DownloadQueueItem missing = new DownloadQueueItem(item.Url, DownloadMode.TranscriptOnly,
            VideoResolution.Best, false, false, 1, "Transcript", folder);
        missing.TranscriptLanguage = "ja";
        manager.AddItems(new[] { missing });
        await Until(() => runner.Calls.Count == 3);
        runner.Calls[2].Output(source);
        runner.Calls[2].Result.SetResult(0);
        await Until(() => missing.Status == QueueItemStatus.Error);
        Assert(runner.Calls.Count == 3 && missing.Failure.Message == "No captions available in this language." &&
            missing.OutputFilePath == null, "Missing language must report a clear error without starting a download");

        DownloadQueueItem noOutput = new DownloadQueueItem(item.Url, DownloadMode.TranscriptOnly,
            VideoResolution.Best, false, false, 1, "Transcript", folder);
        noOutput.TranscriptLanguage = "es";
        noOutput.TranscriptFormat = TranscriptFormat.Vtt;
        manager.AddItems(new[] { noOutput });
        manager.ProcessQueue("fake", null, 1);
        runner.Calls[3].Output(source);
        runner.Calls[3].Result.SetResult(0);
        await Until(() => runner.Calls.Count == 5);
        Assert(runner.Calls[4].Arguments.Contains("--write-auto-subs") && runner.Calls[4].Arguments.Contains("--convert-subs vtt"),
            "The selected VTT and auto-caption settings must reach the engine");
        runner.Calls[4].Result.SetResult(0);
        await Until(() => noOutput.Status == QueueItemStatus.Error);
        Assert(noOutput.OutputFilePath == null, "An engine exit code of zero cannot complete an item without a subtitle file");

        DownloadQueueItem text = new DownloadQueueItem(item.Url, DownloadMode.TranscriptOnly,
            VideoResolution.Best, false, false, 1, "Transcript", folder);
        text.TranscriptFormat = TranscriptFormat.TxtTimed;
        text.InspectedCaptions = TranscriptCaptions.Parse(source);
        manager.AddItems(new[] { text });
        manager.ProcessQueue("fake", null, 1);
        await Until(() => runner.Calls.Count == 6);
        Assert(runner.Calls[5].Arguments.Contains("--skip-download") && runner.Calls[5].Arguments.Contains("--convert-subs srt") &&
            !runner.Calls[5].Arguments.Contains("--dump-single-json"),
            "A preflighted TXT job reuses inspected captions and downloads only the subtitle file");
        File.WriteAllText(Path.Combine(text.TemporaryFolder, "caption.en.srt"), srt);
        runner.Calls[5].Result.SetResult(0);
        await Until(() => text.Status == QueueItemStatus.Completed || text.Status == QueueItemStatus.Error);
        Assert(text.Status == QueueItemStatus.Completed && Path.GetFileName(text.OutputFilePath) == "Lesson [en].txt" &&
            File.ReadAllText(text.OutputFilePath).TrimEnd() == "[00:00:01.000] A timed caption.",
            "The queue should publish a timed TXT file with the cue start time");
        await manager.ShutdownAsync();
        Assert(!Directory.Exists(text.TemporaryFolder), "TXT jobs clean up intermediate subtitle files");
    }

    private static async Task FailureFolderCleanup()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem empty = Item();
        manager.AddItems(new[] { empty });
        manager.ProcessQueue("fake", null, 1);
        Assert(Directory.Exists(empty.TemporaryFolder), "Temporary folder exists while engine is running");
        runner.Calls[0].Result.SetResult(1);
        await Until(() => empty.Status == QueueItemStatus.Error);
        Assert(!Directory.Exists(empty.TemporaryFolder), "Failed source must leave no empty temporary folder");
        manager.RemoveFinished(empty);

        DownloadQueueItem partial = Item();
        manager.AddItems(new[] { partial });
        manager.ProcessQueue("fake", null, 1);
        string fragment = Path.Combine(partial.TemporaryFolder, "video.part");
        string output = Path.Combine(testFolder, "keep-finished.mp4");
        File.WriteAllText(fragment, "resume-me");
        File.WriteAllText(output, "finished");
        Assert(!manager.RemoveFinished(partial) && File.Exists(fragment), "Removal cannot clean a running download");
        runner.Calls[1].Result.SetResult(1);
        await Until(() => partial.Status == QueueItemStatus.Error);
        Assert(File.ReadAllText(fragment) == "resume-me", "Failure preserves partial download");
        manager.Retry(partial);
        Assert(runner.Calls.Count == 3 && File.Exists(fragment), "Retry retains resumable partial data");
        runner.Calls[2].Result.SetResult(1);
        await Until(() => partial.Status == QueueItemStatus.Error);
        Assert(manager.RemoveFinished(partial), "Failed card can be removed");
        await manager.ShutdownAsync();
        Assert(!Directory.Exists(partial.TemporaryFolder) && File.ReadAllText(output) == "finished", "Removal cleans temporary files and preserves finished output");
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--child")
        {
            Console.WriteLine("ready");
            Console.Error.WriteLine("diagnostic");
            if (args[1] == "wait") Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        testFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testFolder);
        Assert((int)DownloadMode.BestAudioOnlyFlac == 4 && (int)DownloadMode.BestAudioOnlyWav == 5, "WAV must append without changing saved format indexes");
        DownloadQueueItem wav = new DownloadQueueItem("https://example.com/audio", DownloadMode.BestAudioOnlyWav,
            VideoResolution.Uhd2160p, true, true, 1, "WAV", testFolder);
        wav.EmbedSubtitles = true; wav.IncludeAutoCaptions = true;
        string wavArguments = YtDlpCommandBuilder.BuildArguments(wav, null);
        Assert(wavArguments.Contains("-f \"ba/b\" -x --audio-format wav") && wavArguments.Contains("ExtractAudio+ffmpeg_o:-c:a pcm_s16le"),
            "WAV must extract best audio as explicit 16-bit PCM");
        Assert(!wavArguments.Contains("-ar ") && !wavArguments.Contains("-ac "), "WAV must preserve source sample rate and channel count");
        Assert(wavArguments.Contains("--embed-metadata ") && wavArguments.Contains("--no-embed-chapters") &&
            !wavArguments.Contains("--embed-thumbnail") && !wavArguments.Contains("--convert-thumbnails") &&
            !wavArguments.Contains("--embed-chapters") && !wavArguments.Contains("--write-subs") && !wavArguments.Contains("--write-auto-subs"),
            "WAV must keep basic metadata without unsupported artwork, chapters or subtitles");
        Assert(wav.EmbedThumbnail && wav.EmbedMetadata, "WAV command generation must not mutate user embedding preferences");
        Assert(YtDlpCommandBuilder.GetTargetExtension(wav.Mode) == ".wav", "WAV must use the correct extension for rename and duplicate handling");
        Assert(YtDlpCommandBuilder.BuildBadgeLabel(wav.Mode, wav.Resolution, false, true, 1) == "WAV   ·   16-BIT PCM",
            "WAV badge must identify PCM without claiming unsupported artwork");
        CompletedDownloadDetails wavDetails = CompletedDownloadDetails.Parse("304|3840|2160|25|wav");
        string wavFile = Path.Combine(testFolder, "summary.wav");
        File.WriteAllBytes(wavFile, new byte[1500]);
        wavDetails = CompletedDownloadDetails.ReadFileAsync(wavFile, wavDetails).GetAwaiter().GetResult();
        Assert(wavDetails.GetSummary(wav.Mode) == "05:04   ·   1.5 KB   ·   WAV", "Completed WAV summary must contain duration, actual size and format without video details");
        DownloadQueueItem flac = new DownloadQueueItem("https://example.com/audio", DownloadMode.BestAudioOnlyFlac,
            VideoResolution.Best, true, true, 1, "FLAC", testFolder);
        string flacArguments = YtDlpCommandBuilder.BuildArguments(flac, null);
        Assert(flacArguments.Contains("--audio-format flac") && flacArguments.Contains("--embed-thumbnail") && flacArguments.Contains("--embed-chapters"),
            "Existing formats must retain their embedding behavior");
        Assert(!DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/shorts/abcdefghijk"), "A Shorts permalink must bypass the playlist picker");
        Assert(!DownloadUrlValidator.IsPlaylistUrl("https://m.youtube.com/shorts/abcdefghijk?si=share&list=PL123"), "A Shorts permalink with share or playlist parameters must remain a single video");
        Assert(DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/@creator/shorts"), "A channel Shorts tab must remain a listing");
        Assert(DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/playlist?list=PL123"), "Explicit playlists must retain the playlist picker");
        Assert(!DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/watch?v=abcdefghijk"), "Ordinary video links must remain single videos");
        DownloadQueueItem stageItem = Item();
        YtDlpOutputParser.ParseLine(stageItem, "[info] test: Downloading 1 format(s): 137+140", null, null);
        Assert(stageItem.StatusText == "Resolving best format", "Format selection should be visible");
        YtDlpOutputParser.ParseLine(stageItem, "[download] Destination: test.mp4", null, null);
        Assert(stageItem.StatusText == "Preparing files" && stageItem.OutputFilePath == "test.mp4", "Preparation must retain destination tracking");
        YtDlpOutputParser.ParseLine(stageItem, "STASH_PROGRESS:avc1|none|15%|1MiB/s|00:10", null, null);
        Assert(stageItem.StatusText == "Downloading video" && stageItem.Status == QueueItemStatus.Downloading, "Progress should replace waiting state");
        YtDlpOutputParser.ParseLine(stageItem, "STASH_FINAL:finished.mp4", null, null);
        Assert(stageItem.StatusText == "Finalizing output" && stageItem.OutputFilePath == "finished.mp4", "Finalization must retain output path");
        List<string> buffered = new List<string>();
        using (ProgressOutputBuffer buffer = new ProgressOutputBuffer((line, error) => buffered.Add(line)))
        {
            for (int i = 0; i < 1000; i++) buffer.Add("STASH_PROGRESS:" + i, false);
            buffer.Add("[Merger] starting", false);
            Assert(buffered.Count < 1000 && buffered[buffered.Count - 2] == "STASH_PROGRESS:999" &&
                buffered[buffered.Count - 1] == "[Merger] starting", "Coalesced progress preserves latest update before stage change");
            buffer.Add("STASH_PROGRESS:final", false);
        }
        Assert(buffered[buffered.Count - 1] == "STASH_PROGRESS:final", "Disposal flushes final buffered progress");
        DownloadQueueItem progressItem = Item();
        YtDlpOutputParser.ParseLine(progressItem, "STASH_PROGRESS:avc1|none|100.0%|1.00MiB/s|00:00", null, null);
        Assert(progressItem.StatusText == "Downloading video" && progressItem.ProgressPercent == 100, "Video stage");
        YtDlpOutputParser.ParseLine(progressItem, "STASH_PROGRESS:none|mp4a|12.0%|1.00MiB/s|00:01", null, null);
        Assert(progressItem.StatusText == "Downloading audio" && progressItem.ProgressPercent == 12, "Audio stage after video");
        YtDlpOutputParser.ParseLine(progressItem, "[Merger] Merging formats into \"test.mp4\"", null, null);
        Assert(progressItem.StatusText == "Combining files..." && progressItem.ProgressPercent == 100, "Combining stage");
        YtDlpOutputParser.ParseLine(progressItem, "STASH_PROGRESS:avc1|mp4a|25%|NA|NA", null, null);
        Assert(progressItem.StatusText == "Downloading video + audio", "Combined stream stage");
        YtDlpOutputParser.ParseLine(progressItem, "STASH_PROGRESS:NA|NA|Unknown%|NA|NA", null, null);
        Assert(progressItem.StatusText == "Downloading" && progressItem.ProgressPercent == 25, "Unknown progress fallback");
        AppDataStorageTests.Run(Path.Combine(testFolder, "storage"));
        ErrorFeatureTests.Run(Path.Combine(testFolder, "logs"));
        UpdateServiceTests.Run(Path.Combine(testFolder, "updates"));
        RegressionTests.Run(Path.Combine(testFolder, "regressions"));
        CompletedDownloadDetails media = CompletedDownloadDetails.Parse("3661.5|1080|1920|29.97|mkv");
        Assert(media.GetSummary(DownloadMode.BestVideoAndAudioMkv) == "1:01:01   ·   MKV   ·   1080×1920 (Full HD)   ·   29.97 fps",
            "Completed metadata must support long durations, portrait resolution and fractional fps");
        Assert(media.GetSummary(DownloadMode.BestAudioOnlyMp3) == "1:01:01   ·   MKV", "Audio summaries must omit source video dimensions and fps");
        Assert(CompletedDownloadDetails.Parse("NA|NA|NA|NA|mp3").GetSummary(DownloadMode.BestAudioOnlyMp3) == "MP3   ·   Completed",
            "Unavailable metadata must not be guessed or displayed as NA");
        Assert(CompletedDownloadDetails.Parse("NaN|-1|Infinity|0|mp4").GetSummary(DownloadMode.BestVideoAndAudioMp4) == "MP4   ·   Completed",
            "Invalid metadata must not leak into completed summaries");
        Assert(CompletedDownloadDetails.Parse("invalid") == null, "Malformed metadata lines must be ignored");
        CompletedDownloadDetails missingFile = CompletedDownloadDetails.ReadFileAsync(Path.Combine(testFolder, "missing-output.mp4"), media).GetAwaiter().GetResult();
        Assert(!missingFile.FileSizeBytes.HasValue && missingFile.DurationSeconds == media.DurationSeconds,
            "Missing files must preserve known metadata without failing completion");
        UiContext context = new UiContext();
        SynchronizationContext.SetSynchronizationContext(context);
        Task tests = RunAll();
        DateTime deadline = DateTime.UtcNow.AddSeconds(45);
        while (!tests.IsCompleted && DateTime.UtcNow < deadline) context.Pump();
        if (!tests.IsCompleted) { Console.Error.WriteLine("FAIL: suite timed out"); return 1; }
        if (tests.IsFaulted) { Console.Error.WriteLine(tests.Exception.GetBaseException()); return 1; }
        Console.WriteLine("PASS: " + assertions + " assertions");
        return 0;
    }
}
