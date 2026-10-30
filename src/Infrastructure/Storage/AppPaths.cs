using System;
using System.IO;

namespace MediaDownloader
{
    // Resolve per-user storage through Windows, independently of the EXE location.
    public sealed class AppPaths
    {
        public static readonly AppPaths Current = new AppPaths(
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        private readonly string executableDirectory;
        public string DataDirectory { get; private set; }
        public string SettingsPath { get; private set; }
        public string EnginesDirectory { get; private set; }

        internal AppPaths(string executableDirectory, string localAppDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(localAppDataDirectory) || !Path.IsPathRooted(localAppDataDirectory))
                throw new InvalidOperationException("Windows did not provide a valid Local AppData folder.");
            this.executableDirectory = Path.GetFullPath(executableDirectory);
            DataDirectory = Path.Combine(localAppDataDirectory, "Stash");
            SettingsPath = Path.Combine(DataDirectory, "settings.cfg");
            EnginesDirectory = Path.Combine(DataDirectory, "bin");
        }

        public void Initialize()
        {
            Directory.CreateDirectory(EnginesDirectory);
            MigrateFile(Path.Combine(executableDirectory, "settings.cfg"), SettingsPath);
            foreach (string engine in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe" })
            {
                string destination = Path.Combine(EnginesDirectory, engine);
                MigrateFile(Path.Combine(executableDirectory, "bin", engine), destination);
                MigrateFile(Path.Combine(executableDirectory, engine), destination);
            }

            // Remove the old engine directory only when empty. Leave unrelated files alone.
            string legacyBin = Path.Combine(executableDirectory, "bin");
            if (!string.Equals(legacyBin, EnginesDirectory, StringComparison.OrdinalIgnoreCase))
            {
                try { if (Directory.Exists(legacyBin)) Directory.Delete(legacyBin, false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void MigrateFile(string source, string destination)
        {
            // Existing per-user data always wins, including when running another EXE copy.
            if (File.Exists(destination) || !File.Exists(source)) return;
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool migrated = false;
            try
            {
                File.Copy(source, temporary, false);
                // A read-only source must not make the user's new settings or
                // installed engines read-only as well.
                File.SetAttributes(temporary, File.GetAttributes(temporary) & ~FileAttributes.ReadOnly);
                try
                {
                    File.Move(temporary, destination);
                    migrated = true;
                }
                catch (IOException)
                {
                    // Another Stash instance may have finished migrating first.
                    if (!File.Exists(destination)) throw;
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            if (migrated)
            {
                // A read-only flash drive can still be used: keep its original copy
                // when removal is forbidden, and use the migrated copy from now on.
                try { File.Delete(source); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
