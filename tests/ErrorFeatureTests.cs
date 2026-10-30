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
            for (int i = 0; i < 500; i++) log.Write(new string('x', 16000));
        FileInfo[] files = new DirectoryInfo(directory).GetFiles("stash-*.log");
        if (files.Length != 5) throw new Exception("Log retention failed: " + files.Length);
        foreach (FileInfo file in files)
            if (file.Length > 1024 * 1024) throw new Exception("Log size limit failed");
        Console.WriteLine("PASS: error fallback and bounded log rotation");
    }
}
