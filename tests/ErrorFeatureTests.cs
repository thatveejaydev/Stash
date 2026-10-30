using System;
using System.IO;
using MediaDownloader;

internal static class ErrorFeatureTests
{
    public static void Run(string directory)
    {
        if (!DownloadFailure.Create("Downloading", "ERROR: Private video").Message.Contains("private")) throw new Exception("Private video classification failed");
        DownloadFailure unknown = DownloadFailure.Create("Downloading", "new engine failure 123");
        if (!unknown.TechnicalDetails.Contains("123") || !unknown.Message.Contains("unexpected")) throw new Exception("Unknown error fallback failed");
        using (RecentLogWriter log = new RecentLogWriter(directory))
            for (int i = 0; i < 500; i++) log.WriteError(new string('x', 16000));
        FileInfo[] files = new DirectoryInfo(directory).GetFiles("stash-*.log");
        if (files.Length != 5) throw new Exception("Log retention failed: " + files.Length);
        foreach (FileInfo file in files)
            if (file.Length > 1024 * 1024) throw new Exception("Log size limit failed");
        string shutdownDirectory = Path.Combine(directory, "shutdown");
        using (RecentLogWriter log = new RecentLogWriter(shutdownDirectory))
        {
            for (int i = 0; i < 20; i++) log.Write("shutdown-line-" + i);
            log.WriteError("test failure");
            log.Write("successful later download");
        }
        string[] saved = File.ReadAllLines(Directory.GetFiles(shutdownDirectory, "stash-*.log")[0]);
        if (saved.Length != 21 || !saved[19].EndsWith("shutdown-line-19") || !saved[20].Contains("test failure"))
            throw new Exception("Shutdown must drain pending log lines in order");
        string successDirectory = Path.Combine(directory, "success");
        using (RecentLogWriter log = new RecentLogWriter(successDirectory))
            for (int i = 0; i < 1000; i++) log.Write("Completed successfully");
        if (Directory.Exists(successDirectory)) throw new Exception("Successful activity must not create logs");
        string boundedDirectory = Path.Combine(directory, "bounded");
        using (RecentLogWriter log = new RecentLogWriter(boundedDirectory))
        {
            for (int i = 0; i < 1000; i++) log.Write("context-line-" + i);
            log.WriteError("failure");
        }
        string[] context = File.ReadAllLines(Directory.GetFiles(boundedDirectory)[0]);
        if (context.Length != 65 || !context[0].EndsWith("context-line-936"))
            throw new Exception("Only bounded recent context should be saved");
        Console.WriteLine("PASS: error fallback and bounded log rotation");
    }
}
