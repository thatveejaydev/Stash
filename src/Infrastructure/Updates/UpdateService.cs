using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MediaDownloader
{
    public sealed class UpdateInfo
    {
        public string Version { get; internal set; }
        public string ReleaseNotes { get; internal set; }
        public string DownloadUrl { get; internal set; }
        public string ChecksumUrl { get; internal set; }
        public string ReleaseUrl { get; internal set; }
    }

    public static class SemanticVersion
    {
        public static bool TryParse(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string text = value.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            string[] parts = text.Split('.');
            int major, minor, patch;
            if (parts.Length != 3 || !int.TryParse(parts[0], out major) ||
                !int.TryParse(parts[1], out minor) || !int.TryParse(parts[2], out patch) ||
                major < 0 || minor < 0 || patch < 0) return false;
            version = new Version(major, minor, patch);
            return true;
        }
    }

    public sealed class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/thatveejaydev/Stash/releases/latest";
        private readonly string updatesDirectory;

        public UpdateService(string updatesDirectory) { this.updatesDirectory = updatesDirectory; }

        public static string InstalledVersion
        {
            get
            {
                AssemblyInformationalVersionAttribute attribute = (AssemblyInformationalVersionAttribute)
                    Attribute.GetCustomAttribute(Assembly.GetExecutingAssembly(), typeof(AssemblyInformationalVersionAttribute));
                return attribute == null ? "0.0.0" : attribute.InformationalVersion;
            }
        }

        public async Task<UpdateInfo> CheckAsync()
        {
            string json = await DownloadTextAsync(LatestReleaseApi);
            return ParseRelease(json, InstalledVersion);
        }

        internal static UpdateInfo ParseRelease(string json, string installedVersion)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            ReleaseDto release = serializer.Deserialize<ReleaseDto>(json);
            if (release == null || release.draft || release.prerelease) return null;
            if (string.IsNullOrWhiteSpace(release.tag_name) || !release.tag_name.StartsWith("v", StringComparison.Ordinal)) return null;
            Version available, installed;
            if (!SemanticVersion.TryParse(release.tag_name, out available) ||
                !SemanticVersion.TryParse(installedVersion, out installed) || available <= installed) return null;
            AssetDto executable = null, checksum = null;
            if (release.assets != null)
            {
                foreach (AssetDto asset in release.assets)
                {
                    if (asset == null) continue;
                    if (string.Equals(asset.name, "Stash.exe", StringComparison.OrdinalIgnoreCase)) executable = asset;
                    if (string.Equals(asset.name, "Stash.exe.sha256", StringComparison.OrdinalIgnoreCase)) checksum = asset;
                }
            }
            if (executable == null || checksum == null || string.IsNullOrWhiteSpace(executable.browser_download_url) ||
                string.IsNullOrWhiteSpace(checksum.browser_download_url)) return null;
            return new UpdateInfo
            {
                Version = available.ToString(3), ReleaseNotes = release.body ?? "",
                DownloadUrl = executable.browser_download_url, ChecksumUrl = checksum.browser_download_url,
                ReleaseUrl = release.html_url
            };
        }

        public async Task<string> DownloadAndVerifyAsync(UpdateInfo update)
        {
            Directory.CreateDirectory(updatesDirectory);
            string expected = ParseChecksum(await DownloadTextAsync(update.ChecksumUrl));
            string destination = Path.Combine(updatesDirectory, "Stash-" + update.Version + ".exe.download");
            string temporary = destination + ".tmp";
            try
            {
                await DownloadFileAsync(update.DownloadUrl, temporary);
                string actual = ComputeSha256(temporary);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temporary, destination);
                return destination;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static string ParseChecksum(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The update checksum is empty.");
            string hash = text.Trim().Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
            if (hash.Length != 64) throw new InvalidDataException("The update checksum is invalid.");
            for (int i = 0; i < hash.Length; i++) if (!Uri.IsHexDigit(hash[i])) throw new InvalidDataException("The update checksum is invalid.");
            return hash;
        }

        internal static string ComputeSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder result = new StringBuilder(64);
                foreach (byte value in hash) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        public static bool CanReplaceExecutable(string executablePath)
        {
            string directory = Path.GetDirectoryName(executablePath);
            string probe = Path.Combine(directory, ".stash-update-write-test-" + Guid.NewGuid().ToString("N"));
            try { File.WriteAllText(probe, "test"); File.Delete(probe); return true; }
            catch { try { if (File.Exists(probe)) File.Delete(probe); } catch { } return false; }
        }

        public string CreateAndLaunchHelper(string downloadedExecutable, string targetExecutable, int parentPid)
        {
            Directory.CreateDirectory(updatesDirectory);
            string helper = Path.Combine(updatesDirectory, "Stash.Updater." + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(targetExecutable, helper, false);
            Process.Start(new ProcessStartInfo
            {
                FileName = helper,
                Arguments = "--apply-update " + Quote(downloadedExecutable) + " " + Quote(targetExecutable) + " " + parentPid,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(targetExecutable)
            });
            return helper;
        }

        public void CleanupStaleFiles()
        {
            if (!Directory.Exists(updatesDirectory)) return;
            foreach (string path in Directory.GetFiles(updatesDirectory))
            {
                try { if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7)) File.Delete(path); }
                catch { }
            }
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }

        private static Task<string> DownloadTextAsync(string url)
        {
            return Task.Run(() =>
            {
                HttpWebRequest request = CreateRequest(url);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            });
        }

        private static Task DownloadFileAsync(string url, string path)
        {
            return Task.Run(() =>
            {
                HttpWebRequest request = CreateRequest(url);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            });
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Stash-Windows-Updater/" + InstalledVersion;
            request.Accept = "application/vnd.github+json";
            request.Timeout = 15000; request.ReadWriteTimeout = 30000;
            return request;
        }

        private sealed class ReleaseDto
        {
            public string tag_name { get; set; } public bool draft { get; set; } public bool prerelease { get; set; }
            public string body { get; set; } public string html_url { get; set; } public AssetDto[] assets { get; set; }
        }
        private sealed class AssetDto { public string name { get; set; } public string browser_download_url { get; set; } }
    }
}
