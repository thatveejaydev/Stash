using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaDownloader;

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
        DownloadQueueItem first = Item(), second = Item(), third = Item();
        manager.AddItems(new[] { first, second, third });
        manager.ProcessQueue("fake", null, 2);
        Assert(runner.Calls.Count == 2 && third.Status == QueueItemStatus.Queued, "Parallel limit must hold");
        manager.TogglePauseResume(third);
        Assert(third.Status == QueueItemStatus.Paused, "Queued item can pause without starting a process");
        runner.Calls[0].Output("[download] 25.0% of 10MiB at 1MiB/s ETA 00:10");
        await Until(() => first.ProgressPercent == 25);
        Assert(first.Speed == "8.4 Mbps", "Output should be parsed on the owner thread");
        runner.Calls[0].Result.SetResult(0);
        await Until(() => first.Status == QueueItemStatus.Completed);
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
        await manager.ShutdownAsync();
    }

    private static async Task MetadataCancellationAndRename()
    {
        FakeRunner runner = new FakeRunner();
        DownloadManager manager = Manager(runner);
        DownloadQueueItem item = Item(false);
        manager.AddItems(new[] { item });
        manager.ProcessQueue("fake", null, 1);
        Assert(runner.Calls[0].Arguments.Contains("--get-title"), "Metadata must use the managed process runner");
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
        Console.WriteLine("Queue and parallelism");
        await QueueAndParallelism();
        Console.WriteLine("Pause/resume and stale output");
        await PauseResumeAndStaleOutput();
        Console.WriteLine("Cancel overrides pause");
        await CancelOverridesPause();
        Console.WriteLine("Metadata cancellation and rename");
        await MetadataCancellationAndRename();
        Console.WriteLine("Shutdown and startup failure");
        await ShutdownAndStartupFailure();
        Console.WriteLine("Real process ownership");
        await RealProcessOwnership();
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
        AppDataStorageTests.Run(Path.Combine(testFolder, "storage"));
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
