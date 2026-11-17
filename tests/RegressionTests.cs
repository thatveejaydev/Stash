using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Stash;

// Covers the fixes from the code audit: argument quoting, drive-root folders, error
// classification, duplicate detection, settings and queue persistence, playlist parsing.
internal static class RegressionTests
{
    private static int assertions;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    // Parses arguments exactly as the child process will.
    private static string[] SplitArguments(string arguments)
    {
        int count;
        IntPtr argv = CommandLineToArgvW("program.exe " + arguments, out count);
        try
        {
            string[] result = new string[count - 1];
            for (int i = 1; i < count; i++) result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
            return result;
        }
        finally { LocalFree(argv); }
    }

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        Console.WriteLine("  regression: Quoting"); Quoting();
        Console.WriteLine("  regression: DriveRootFolders"); DriveRootFolders();
        Console.WriteLine("  regression: FailureClassification"); FailureClassification();
        Console.WriteLine("  regression: PlaylistDetection"); PlaylistDetection();
        Console.WriteLine("  regression: DuplicateDetection"); DuplicateDetection(Path.Combine(root, "duplicates"));
        Console.WriteLine("  regression: FileNames"); FileNames();
        Console.WriteLine("  regression: SettingsPersistence"); SettingsPersistence(Path.Combine(root, "settings"));
        Console.WriteLine("  regression: QueuePersistence"); QueuePersistence(Path.Combine(root, "queue"));
        Console.WriteLine("  regression: PlaylistParsing"); PlaylistParsing();
        Console.WriteLine("  regression: Checksums"); Checksums();
        Console.WriteLine("  regression: TemporaryFolderSweep"); TemporaryFolderSweep(Path.Combine(root, "sweep"));
        Console.WriteLine("PASS: " + assertions + " regression assertions");
    }

    private static void Quoting()
    {
        string[] samples = { "D:\\", "C:\\Users\\Me\\", "plain", "with space", "quote\"inside", "trailing\\\\", "a\\\"b", "" };
        foreach (string sample in samples)
        {
            string[] parsed = SplitArguments(CommandLine.Quote(sample) + " next");
            Assert(parsed.Length == 2 && parsed[0] == sample && parsed[1] == "next", "Quoting must round-trip: " + sample);
        }
    }

    private static void DriveRootFolders()
    {
        DownloadQueueItem item = new DownloadQueueItem("https://example.com/watch?v=1", DownloadMode.BestVideoAndAudioMp4,
            VideoResolution.Best, true, true, 4, "MP4", "C:\\");
        string[] args = SplitArguments(YtDlpCommandBuilder.BuildArguments(item, null));
        int home = Array.IndexOf(args, "home:C:\\");
        Assert(home > 0 && args[home - 1] == "--paths", "Drive root output folder must reach yt-dlp intact");
        Assert(Array.IndexOf(args, "temp:" + item.TemporaryFolder) > 0, "Temporary folder argument must follow intact");
        Assert(args[args.Length - 1] == item.Url, "URL must be the final argument");
        Assert(item.TemporaryFolder.StartsWith("C:\\.stash-temp-", StringComparison.OrdinalIgnoreCase), "Temporary folder sits in the drive root");
        // Previously threw "Invalid temporary folder" and left the card stuck.
        // Task.Run: earlier suites leave a WinForms synchronization context on this thread.
        System.Threading.Tasks.Task.Run(() => StorageFileService.CleanupEmptyTemporaryFolderAsync(item)).Wait();
        Assert(System.Threading.Tasks.Task.Run(() => StorageFileService.CleanupPartialFilesForItemAsync(item)).Result == 0, "Drive-root cleanup must validate the folder");
        Assert(StorageFileService.SameDirectory("C:\\", "c:\\") && StorageFileService.SameDirectory("C:\\Temp\\", "C:\\Temp"),
            "Directory comparison must ignore case and trailing separators");
        Assert(StorageFileService.NormalizeDirectory("C:\\") == "C:\\", "Drive roots keep their separator");

        DownloadQueueItem quoted = new DownloadQueueItem("https://example.com/a?b=\"c\"", DownloadMode.BestAudioOnlyMp3,
            VideoResolution.Best, false, false, 1, "MP3", "C:\\Music\\");
        quoted.CustomBaseFileName = "My \"Song\"";
        string[] quotedArgs = SplitArguments(YtDlpCommandBuilder.BuildArguments(quoted, null));
        Assert(quotedArgs[quotedArgs.Length - 1] == quoted.Url, "Quotes in URLs must not split arguments");
        Assert(Array.IndexOf(SplitArguments(YtDlpCommandBuilder.BuildMetadataArguments(quoted)), quoted.Url) >= 0, "Metadata lookup must pass the URL intact");
    }

    private static void FailureClassification()
    {
        Func<string, string> message = details => DownloadFailure.Create("Downloading", details).Message;
        Assert(message("ERROR: Unable to download webpage: <urlopen error timed out>").Contains("internet"), "Webpage timeouts are network errors");
        Assert(message("ERROR: [youtube] abc: Sign in to confirm your age. This video may be inappropriate for some users.").Contains("access"), "Age gates need access");
        Assert(message("ERROR: [youtube] abc: Sign in to confirm you're not a bot").Contains("access"), "Bot checks need cookies");
        Assert(message("ERROR: unable to write image message storage usage page").Contains("unexpected"), "Words containing 'age' must not be misread");
        Assert(message("ERROR: HTTP Error 429: Too Many Requests").Contains("limiting"), "Rate limits are recognised");
        Assert(message("ERROR: [youtube] abc: Requested format is not available").Contains("format"), "Unavailable formats are recognised");
        Assert(message("ERROR: [youtube] abc: Video unavailable").Contains("no longer available"), "Unavailable videos are recognised");
        Assert(message("ERROR: Private video. Sign in if you've been granted access").Contains("private"), "Private videos win over sign-in");
        Assert(message("OSError: [Errno 28] No space left on device").Contains("storage"), "Disk full is recognised");
        Assert(message("ERROR: Unsupported URL: https://example.com").Contains("supported"), "Unsupported sites are recognised");
        Assert(message("PermissionError: [Errno 13] Permission denied").Contains("cannot write"), "Permission errors are recognised");
        Assert(DownloadFailure.Create("Merging", "something odd").Message.Contains("processing"), "Merging fallback remains");
    }

    private static void PlaylistDetection()
    {
        Assert(DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/playlist?list=PL1"), "Playlist page");
        Assert(DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/watch?v=abcdefghijk&list=PL1"), "Watch inside playlist");
        Assert(DownloadUrlValidator.IsPlaylistUrl("https://www.youtube.com/@creator/videos"), "Channel videos tab");
        Assert(!DownloadUrlValidator.IsPlaylistUrl("https://vimeo.com/123456"), "Single Vimeo video");
        string single, id;
        Assert(DownloadUrlValidator.TryExtractSingleVideoUrl("https://youtu.be/abcdefghijk?list=PL1", out single, out id) && id == "abcdefghijk",
            "Short links inside playlists expose the single video");
        List<string> urls = DownloadUrlValidator.ExtractUrls("see https://a.example/x, and (https://b.example/y) twice https://a.example/x.");
        Assert(urls.Count == 2, "Extraction trims punctuation and removes duplicates");
    }

    private static void DuplicateDetection(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Introduction to X.mp4"), "x");
        File.WriteAllText(Path.Combine(folder, "Song.mp3"), "x");
        File.WriteAllText(Path.Combine(folder, "Clip [abcdefghijk].mkv"), "x");
        File.WriteAllText(Path.Combine(folder, "Partial.mp4.part"), "x");
        Assert(StorageFileService.FindSimilarExistingFile(folder, "Intro", null) == null, "A prefix of another title is not a duplicate");
        Assert(StorageFileService.FindSimilarExistingFile(folder, "Song", null) == "Song.mp3", "Exact base names are duplicates");
        Assert(StorageFileService.FindSimilarExistingFile(folder, "Clip", null) == "Clip [abcdefghijk].mkv", "Titles tagged with an id match");
        Assert(StorageFileService.FindSimilarExistingFile(folder, "Other", "abcdefghijk") == "Clip [abcdefghijk].mkv", "Video ids match");
        Assert(StorageFileService.FindSimilarExistingFile(folder, "Partial.mp4", null) == null, "Partial files are ignored");
        Assert(StorageFileService.SuggestNextAvailableName(folder, "Song", ".mp3", name => name == "Song (2)") == "Song (3)",
            "Suggestions skip names reserved by queued downloads");
    }

    private static void FileNames()
    {
        Assert(YtDlpCommandBuilder.BuildBaseFileName(FileNameStyle.Title, "My: Video", "id1", "Chan") == "My_ Video", "Title style sanitizes");
        Assert(YtDlpCommandBuilder.BuildBaseFileName(FileNameStyle.TitleWithId, "My Video", "dQw4w9WgXcQ", null) == "My Video [dQw4w9WgXcQ]", "Id style");
        Assert(YtDlpCommandBuilder.BuildBaseFileName(FileNameStyle.UploaderAndTitle, "My Video", "id", "Channel") == "Channel - My Video", "Uploader style");
        Assert(YtDlpCommandBuilder.BuildBaseFileName(FileNameStyle.UploaderAndTitle, "My Video", "id", "NA") == "My Video", "Missing uploader falls back");
        string longName = YtDlpCommandBuilder.BuildBaseFileName(FileNameStyle.TitleWithId, new string('x', 300), "abcdefghijk", null);
        Assert(longName.Length <= 100 && longName.EndsWith(" [abcdefghijk]"), "Long titles keep their id suffix");
    }

    private static void SettingsPersistence(string folder)
    {
        string path = Path.Combine(folder, "settings.cfg");
        AppSettings settings = AppSettings.Load(path);
        settings.FileNameStyle = FileNameStyle.UploaderAndTitle;
        settings.WatchClipboard = true;
        settings.LastEngineCheckUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        settings.Save();
        Assert(Directory.GetFiles(folder, "*.tmp").Length == 0, "Atomic saves leave no temporary files");
        DateTime written = File.GetLastWriteTimeUtc(path);
        System.Threading.Thread.Sleep(30);
        AppSettings loaded = AppSettings.Load(path);
        Assert(loaded.FileNameStyle == FileNameStyle.UploaderAndTitle && loaded.WatchClipboard &&
            loaded.LastEngineCheckUtc == settings.LastEngineCheckUtc, "New preferences round-trip");
        Assert(File.GetLastWriteTimeUtc(path) == written, "Loading settings must not rewrite the file");
    }

    private static void QueuePersistence(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "queue.json");
        DownloadQueueItem paused = new DownloadQueueItem("https://example.com/p", DownloadMode.BestAudioOnlyFlac, VideoResolution.Best,
            true, false, 8, "FLAC", folder);
        paused.Title = "Paused one"; paused.Status = QueueItemStatus.Paused; paused.ProgressPercent = 42; paused.SourceId = "pid";
        paused.CustomBaseFileName = "Paused one"; paused.MetadataResolved = true; paused.FileNameStyle = FileNameStyle.TitleWithId;
        DownloadQueueItem running = new DownloadQueueItem("https://example.com/r", DownloadMode.BestVideoAndAudioMp4, VideoResolution.Hd720p,
            false, false, 1, "MP4", folder);
        running.Status = QueueItemStatus.Downloading;
        DownloadQueueItem done = new DownloadQueueItem("https://example.com/d", DownloadMode.BestVideoAndAudioMkv, VideoResolution.Best,
            false, false, 1, "MKV", folder);
        done.Status = QueueItemStatus.Completed; done.OutputFilePath = Path.Combine(folder, "d.mkv");
        DownloadQueueItem cancelled = new DownloadQueueItem("https://example.com/c", DownloadMode.BestVideoAndAudioMp4, VideoResolution.Best,
            false, false, 1, "MP4", folder);
        cancelled.Status = QueueItemStatus.Cancelled;

        QueueStore store = new QueueStore(path);
        store.Save(new List<DownloadQueueItem> { paused, running, done, cancelled });
        List<DownloadQueueItem> restored = store.Load();
        Assert(restored.Count == 3, "Cancelled items are not restored");
        Assert(restored[0].Status == QueueItemStatus.Paused && restored[0].TemporaryFolder == paused.TemporaryFolder &&
            restored[0].ProgressPercent == 42 && restored[0].SourceId == "pid" && restored[0].FileNameStyle == FileNameStyle.TitleWithId &&
            restored[0].CustomBaseFileName == "Paused one" && restored[0].Mode == DownloadMode.BestAudioOnlyFlac,
            "Unfinished items return paused with their partial-file folder");
        Assert(restored[1].Status == QueueItemStatus.Paused && restored[1].Resolution == VideoResolution.Hd720p, "Running items return paused");
        Assert(restored[2].Status == QueueItemStatus.Completed && restored[2].OutputFilePath == done.OutputFilePath, "Completed history is kept");

        File.WriteAllText(path, "{ not json");
        Assert(store.Load().Count == 0, "A corrupt queue file is ignored");
        File.WriteAllText(path, "{\"Version\":1,\"Items\":[{\"Url\":\"file:///c:/x\",\"OutputFolder\":\"C:\\\\\",\"Mode\":0,\"Resolution\":0}," +
            "{\"Url\":\"https://ok.example/v\",\"OutputFolder\":\"C:\\\\Videos\",\"TemporaryFolder\":\"C:\\\\Windows\\\\.stash-temp-x\",\"Mode\":0,\"Resolution\":0}]}");
        List<DownloadQueueItem> hostile = store.Load();
        Assert(hostile.Count == 1 && hostile[0].TemporaryFolder.StartsWith("C:\\Videos\\.stash-temp-", StringComparison.OrdinalIgnoreCase),
            "Restored items reject non-web links and foreign temporary folders");
    }

    private static void PlaylistParsing()
    {
        PlaylistInfo info = new PlaylistInfo();
        ExternalToolService.ParsePlaylistOutput(
            "My List\t5\tid1\tFirst\thttps://www.youtube.com/watch?v=id1\n" +
            "My List\t5\tid2\tSecond\tNA\n" +
            "My List\t5\tid1\tFirst again\thttps://www.youtube.com/watch?v=id1\n" +
            "My List\t5\tid3\tThird\tjavascript:alert(1)\n", info);
        Assert(info.Title == "My List" && info.Count == 3 && info.ReportedCount == 5 && info.UnavailableCount == 2,
            "Playlists with hidden entries remain usable and report what was skipped");
        Assert(info.Entries[1].Url == "https://www.youtube.com/watch?v=id2" && info.Entries[2].Url == "https://www.youtube.com/watch?v=id3",
            "Missing or non-web entry links fall back to the video id");
    }

    private static void Checksums()
    {
        string hash = new string('b', 64);
        string list = new string('a', 64) + "  yt-dlp_x86.exe\n" + hash + "  yt-dlp.exe\n";
        Assert(EngineInstaller.ParseChecksumFor(list, "yt-dlp.exe") == hash, "The checksum for the exact file name is used");
        bool missing = false;
        try { EngineInstaller.ParseChecksumFor(list, "ffmpeg.zip"); } catch (InvalidDataException) { missing = true; }
        Assert(missing, "A missing checksum fails closed");
    }

    private static void TemporaryFolderSweep(string folder)
    {
        string orphan = Path.Combine(folder, ".stash-temp-orphan");
        string active = Path.Combine(folder, ".stash-temp-active");
        string recent = Path.Combine(folder, ".stash-temp-recent");
        string unrelated = Path.Combine(folder, "keep-me");
        foreach (string dir in new[] { orphan, active, recent, unrelated }) Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(orphan, "video.part"), "x");
        File.WriteAllText(Path.Combine(unrelated, "file.txt"), "x");
        Directory.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-2));
        int removed = StorageFileService.SweepOrphanedTemporaryFolders(new[] { folder }, new[] { active }, TimeSpan.FromHours(6));
        Assert(removed == 1 && !Directory.Exists(orphan), "Old unreferenced temporary folders are removed");
        Assert(Directory.Exists(active) && Directory.Exists(recent) && Directory.Exists(unrelated), "Active, recent and unrelated folders are kept");
    }
}
