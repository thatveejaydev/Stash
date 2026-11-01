using System;
using System.IO;
using MediaDownloader;

internal static class UpdateServiceTests
{
    private static int assertions;
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }

    public static void Run(string testRoot)
    {
        Version version;
        Assert(SemanticVersion.TryParse("0.10.2", out version) && version > new Version(0, 9, 99), "Semantic versions must compare components");
        Assert(SemanticVersion.TryParse("v1.2.3", out version) && version == new Version(1, 2, 3), "Optional v prefix must parse");
        Assert(!SemanticVersion.TryParse("1.2", out version) && !SemanticVersion.TryParse("1.2.3-beta", out version), "Malformed versions must be rejected");

        string valid = "{\"tag_name\":\"v0.3.0\",\"draft\":false,\"prerelease\":false,\"body\":\"Notes\",\"html_url\":\"https://example/release\",\"assets\":[" +
            "{\"name\":\"Stash.exe\",\"browser_download_url\":\"https://example/Stash.exe\"}," +
            "{\"name\":\"Stash.exe.sha256\",\"browser_download_url\":\"https://example/Stash.exe.sha256\"}]}";
        UpdateInfo update = UpdateService.ParseRelease(valid, "0.2.0");
        Assert(update != null && update.Version == "0.3.0" && update.ReleaseNotes == "Notes", "New stable release with both assets must parse");
        Assert(UpdateService.ParseRelease(valid, "0.3.0") == null, "Equal version must be ignored");
        Assert(UpdateService.ParseRelease(valid, "0.4.0") == null, "Older version must be ignored");
        Assert(UpdateService.ParseRelease(valid.Replace("v0.3.0", "0.3.0"), "0.2.0") == null, "Release tags must start with v");
        Assert(UpdateService.ParseRelease(valid.Replace("\"prerelease\":false", "\"prerelease\":true"), "0.2.0") == null, "Prereleases must be ignored");
        Assert(UpdateService.ParseRelease(valid.Replace("\"draft\":false", "\"draft\":true"), "0.2.0") == null, "Drafts must be ignored");
        Assert(UpdateService.ParseRelease(valid.Replace("Stash.exe.sha256", "missing.sha256"), "0.2.0") == null, "Missing checksum asset must be ignored");

        string hash = new string('a', 64);
        Assert(UpdateService.ParseChecksum(hash + "  Stash.exe\n") == hash, "Standard checksum sidecar must parse");
        bool invalidChecksum = false;
        try { UpdateService.ParseChecksum("not-a-hash"); } catch (InvalidDataException) { invalidChecksum = true; }
        Assert(invalidChecksum, "Invalid checksums must fail closed");
        Directory.CreateDirectory(testRoot);
        string fixture = Path.Combine(testRoot, "hash-fixture");
        File.WriteAllText(fixture, "stash");
        Assert(UpdateService.ComputeSha256(fixture).Length == 64, "File SHA-256 must be produced");
        string testExecutable = System.Reflection.Assembly.GetExecutingAssembly().Location;
        UpdateService.ValidateExecutableVersion(testExecutable, UpdateService.InstalledVersion);
        bool wrongVersion = false;
        try { UpdateService.ValidateExecutableVersion(testExecutable, "99.0.0"); }
        catch (InvalidDataException) { wrongVersion = true; }
        Assert(wrongVersion, "A checksum-valid executable with the wrong release version must be rejected");

        string target = Path.Combine(testRoot, "installed.exe");
        string source = Path.Combine(testRoot, "downloaded.exe");
        File.WriteAllText(target, "old"); File.WriteAllText(source, "new");
        UpdateApplier.ReplaceAndLaunch(source, target, path =>
            Assert(File.ReadAllText(path) == "new", "Restart must launch the replaced executable"));
        Assert(File.ReadAllText(target) == "new" && !File.Exists(source), "Successful update must retain new bytes and clean the download");
        File.WriteAllText(target, "old"); File.WriteAllText(source, "new");
        bool rolledBack = false;
        try { UpdateApplier.ReplaceAndLaunch(source, target, path => { throw new IOException("simulated startup failure"); }); }
        catch (IOException) { rolledBack = true; }
        Assert(rolledBack && File.ReadAllText(target) == "old", "Failed restart must restore previous executable");
        Assert(Directory.GetFiles(testRoot, "*.backup").Length == 0, "Successful rollback must consume its backup");
        Console.WriteLine("PASS: " + assertions + " updater assertions");
    }
}
