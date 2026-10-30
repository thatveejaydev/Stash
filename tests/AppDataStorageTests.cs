using System;
using System.IO;
using MediaDownloader;

internal static class AppDataStorageTests
{
    private static int assertions;
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        assertions++;
    }

    public static void Run(string testRoot)
    {
        string localData = Path.Combine(testRoot, "profile", "Local");
        string firstLocation = Path.Combine(testRoot, "flash-drive", "Stash");
        string secondLocation = Path.Combine(testRoot, "another-drive", "Stash");
        Directory.CreateDirectory(Path.Combine(firstLocation, "bin"));
        Directory.CreateDirectory(secondLocation);
        AppPaths paths = new AppPaths(firstLocation, localData);
        Assert(paths.SettingsPath == Path.Combine(localData, "Stash", "settings.cfg"), "Settings must use per-user storage");
        Assert(paths.EnginesDirectory == Path.Combine(localData, "Stash", "bin"), "Engines must use per-user storage");
        File.WriteAllText(Path.Combine(firstLocation, "settings.cfg"), "FormatIndex=3\nParallelCount=4\n");
        foreach (string engine in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            File.WriteAllText(Path.Combine(firstLocation, "bin", engine), engine + " fixture");
        paths.Initialize();
        Assert(File.Exists(paths.SettingsPath), "Legacy settings must migrate");
        Assert(!File.Exists(Path.Combine(firstLocation, "settings.cfg")), "Migrated writable settings must leave the EXE directory");
        Assert(!Directory.Exists(Path.Combine(firstLocation, "bin")), "Empty legacy bin must be removed");
        foreach (string engine in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            Assert(File.ReadAllText(Path.Combine(paths.EnginesDirectory, engine)) == engine + " fixture", "Engine contents must survive migration");
        AppSettings settings = AppSettings.Load(paths.SettingsPath);
        Assert(settings.FormatIndex == 3 && settings.ParallelCount == 4, "Migrated preferences must load");
        settings.FormatIndex = 2;
        settings.EmbedSubtitles = false;
        settings.Save();
        AppPaths relocated = new AppPaths(secondLocation, localData);
        relocated.Initialize();
        AppSettings reloaded = AppSettings.Load(relocated.SettingsPath);
        Assert(reloaded.FormatIndex == 2 && !reloaded.EmbedSubtitles, "Moving the EXE must retain saved preferences");
        Assert(!File.Exists(Path.Combine(secondLocation, "settings.cfg")) && !Directory.Exists(Path.Combine(secondLocation, "bin")), "New EXE location must stay clean");

        File.WriteAllText(Path.Combine(secondLocation, "settings.cfg"), "FormatIndex=0");
        Directory.CreateDirectory(Path.Combine(secondLocation, "bin"));
        File.WriteAllText(Path.Combine(secondLocation, "bin", "yt-dlp.exe"), "older engine");
        File.WriteAllText(Path.Combine(secondLocation, "bin", "notes.txt"), "unrelated data");
        relocated.Initialize();
        Assert(AppSettings.Load(paths.SettingsPath).FormatIndex == 2, "Existing AppData preferences must not be overwritten");
        Assert(File.ReadAllText(Path.Combine(paths.EnginesDirectory, "yt-dlp.exe")) == "yt-dlp.exe fixture", "Existing AppData engine must not be overwritten");
        Assert(File.Exists(Path.Combine(secondLocation, "bin", "notes.txt")), "Migration must leave unrelated files alone");

        string freshLocalData = Path.Combine(testRoot, "different-account", "Local");
        AppPaths differentUser = new AppPaths(secondLocation, freshLocalData);
        differentUser.Initialize();
        Assert(differentUser.SettingsPath != paths.SettingsPath, "Different users must have separate storage");
        Assert(AppSettings.Load(differentUser.SettingsPath).FormatIndex == 0, "New user should import only their own legacy settings");

        string cleanLocation = Path.Combine(testRoot, "clean-install");
        Directory.CreateDirectory(cleanLocation);
        AppPaths fresh = new AppPaths(cleanLocation, Path.Combine(testRoot, "fresh-profile"));
        fresh.Initialize();
        Assert(Directory.Exists(fresh.EnginesDirectory), "A fresh install must create engine storage");
        Assert(!File.Exists(ExternalToolService.FindExecutable(fresh.EnginesDirectory, "yt-dlp.exe")), "Missing engines must remain missing for the install prompt");
        AppSettings defaults = AppSettings.Load(fresh.SettingsPath);
        Assert(File.Exists(fresh.SettingsPath) && defaults.ParallelCount == 2, "Fresh preferences must save in AppData");

        string rootEngineLocation = Path.Combine(testRoot, "legacy-root-engines");
        Directory.CreateDirectory(rootEngineLocation);
        File.WriteAllText(Path.Combine(rootEngineLocation, "ffmpeg.exe"), "root engine fixture");
        AppPaths rootEngines = new AppPaths(rootEngineLocation, Path.Combine(testRoot, "root-engine-profile"));
        rootEngines.Initialize();
        Assert(File.Exists(Path.Combine(rootEngines.EnginesDirectory, "ffmpeg.exe")), "Legacy engines beside the EXE must migrate too");

        string readonlyLocation = Path.Combine(testRoot, "readonly-source");
        Directory.CreateDirectory(readonlyLocation);
        string readonlySettings = Path.Combine(readonlyLocation, "settings.cfg");
        File.WriteAllText(readonlySettings, "FormatIndex=4");
        File.SetAttributes(readonlySettings, FileAttributes.ReadOnly);
        try
        {
            AppPaths readonlyPaths = new AppPaths(readonlyLocation, Path.Combine(testRoot, "readonly-profile"));
            readonlyPaths.Initialize();
            Assert(AppSettings.Load(readonlyPaths.SettingsPath).FormatIndex == 4, "Read-only sources must still migrate");
            Assert(File.Exists(readonlySettings), "Undeletable source must remain intact");
            AppSettings writable = AppSettings.Load(readonlyPaths.SettingsPath);
            writable.FormatIndex = 1;
            writable.Save();
            Assert(AppSettings.Load(readonlyPaths.SettingsPath).FormatIndex == 1, "Imported read-only settings must become writable in AppData");
        }
        finally { File.SetAttributes(readonlySettings, FileAttributes.Normal); }
        Console.WriteLine("PASS: " + assertions + " AppData storage assertions");
    }
}
