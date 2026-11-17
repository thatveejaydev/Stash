using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stash
{
    // Installs yt-dlp and FFmpeg into the per-user engine folder. Every download goes to a
    // temporary file, is checked against the publisher's SHA-256 list and is smoke-tested
    // before it replaces the installed copy, so an interrupted download never looks installed.
    internal sealed class EngineInstaller
    {
        internal const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        internal const string YtDlpChecksumsUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS";
        internal const string FfmpegZipName = "ffmpeg-master-latest-win64-gpl.zip";
        internal const string FfmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/" + FfmpegZipName;
        internal const string FfmpegChecksumsUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/checksums.sha256";

        // Reports a short status line and, when known, a 0-100 percentage (-1 when unknown).
        internal delegate void ProgressReporter(string status, int percent);

        private readonly string binFolder;

        internal EngineInstaller(string binFolder) { this.binFolder = binFolder; }

        internal string YtDlpPath { get { return Path.Combine(binFolder, "yt-dlp.exe"); } }
        internal string FfmpegPath { get { return Path.Combine(binFolder, "ffmpeg.exe"); } }
        internal string FfprobePath { get { return Path.Combine(binFolder, "ffprobe.exe"); } }

        internal async Task InstallYtDlpAsync(ProgressReporter progress, CancellationToken cancellation)
        {
            Directory.CreateDirectory(binFolder);
            string download = YtDlpPath + ".download";
            try
            {
                await DownloadFileAsync(YtDlpUrl, download, "Downloading yt-dlp", progress, cancellation);
                progress("Verifying yt-dlp", -1);
                string expected = ParseChecksumFor(await DownloadTextAsync(YtDlpChecksumsUrl, cancellation), "yt-dlp.exe");
                VerifyChecksum(download, expected, "yt-dlp.exe");
                await SmokeTestAsync(download, "--version", cancellation);
                ReplaceFile(download, YtDlpPath);
            }
            finally { TryDelete(download); }
        }

        internal async Task InstallFfmpegAsync(ProgressReporter progress, CancellationToken cancellation)
        {
            Directory.CreateDirectory(binFolder);
            string zip = Path.Combine(binFolder, FfmpegZipName + ".download");
            string ffmpegNew = FfmpegPath + ".new", ffprobeNew = FfprobePath + ".new";
            try
            {
                await DownloadFileAsync(FfmpegUrl, zip, "Downloading FFmpeg", progress, cancellation);
                progress("Verifying FFmpeg", -1);
                // The checksum list is fetched after the archive, so a rebuild published while the
                // archive was downloading is detected as a mismatch instead of being trusted.
                string expected = ParseChecksumFor(await DownloadTextAsync(FfmpegChecksumsUrl, cancellation), FfmpegZipName);
                VerifyChecksum(zip, expected, FfmpegZipName);

                progress("Extracting FFmpeg", -1);
                await Task.Run(() =>
                {
                    bool ffmpegFound = false, ffprobeFound = false;
                    using (ZipArchive archive = ZipFile.OpenRead(zip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (string.Equals(entry.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                            { entry.ExtractToFile(ffmpegNew, true); ffmpegFound = true; }
                            else if (string.Equals(entry.Name, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                            { entry.ExtractToFile(ffprobeNew, true); ffprobeFound = true; }
                        }
                    }
                    if (!ffmpegFound || !ffprobeFound) throw new InvalidDataException("The FFmpeg archive does not contain ffmpeg.exe and ffprobe.exe.");
                }, cancellation);

                await SmokeTestAsync(ffmpegNew, "-version", cancellation);
                await SmokeTestAsync(ffprobeNew, "-version", cancellation);
                ReplaceFile(ffmpegNew, FfmpegPath);
                ReplaceFile(ffprobeNew, FfprobePath);
            }
            finally
            {
                TryDelete(zip);
                TryDelete(ffmpegNew);
                TryDelete(ffprobeNew);
            }
        }

        // Runs yt-dlp's own self-update, which verifies the release it downloads.
        internal static Task<Tuple<int, string>> SelfUpdateYtDlpAsync(string ytDlpPath)
        {
            return Task.Run(() =>
            {
                ProcessStartInfo psi = new ProcessStartInfo(ytDlpPath, "-U")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (Process p = Process.Start(psi))
                {
                    Task<string> error = p.StandardError.ReadToEndAsync();
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(120000))
                    {
                        ExternalToolService.KillProcessTree(p.Id);
                        return Tuple.Create(-1, "yt-dlp update timed out.");
                    }
                    p.WaitForExit();
                    return Tuple.Create(p.ExitCode, (output + "\n" + error.Result).Trim());
                }
            });
        }

        internal static string ParseChecksumFor(string text, string fileName)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = rawLine.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    string name = parts[parts.Length - 1].TrimStart('*');
                    if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    return UpdateService.ParseChecksum(parts[0]);
                }
            }
            throw new InvalidDataException("The publisher's checksum list does not include " + fileName + ".");
        }

        private static void VerifyChecksum(string path, string expected, string displayName)
        {
            if (!string.Equals(UpdateService.ComputeSha256(path), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(displayName + " failed SHA-256 verification. It may have been updated while downloading; try again.");
        }

        private static async Task SmokeTestAsync(string executable, string arguments, CancellationToken cancellation)
        {
            int exitCode = await Task.Run(() =>
            {
                ProcessStartInfo psi = new ProcessStartInfo(executable, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(30000)) { ExternalToolService.KillProcessTree(p.Id); return -1; }
                    return p.ExitCode;
                }
            }, cancellation);
            if (exitCode != 0) throw new InvalidDataException(Path.GetFileName(executable) + " did not start correctly (exit code " + exitCode + ").");
        }

        private static void ReplaceFile(string source, string destination)
        {
            if (File.Exists(destination)) File.Replace(source, destination, null, true);
            else File.Move(source, destination);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Stash-Windows-Downloader/" + UpdateService.InstalledVersion;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 60000;
            request.AllowAutoRedirect = true;
            return request;
        }

        internal static Task<string> DownloadTextAsync(string url, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                HttpWebRequest request = CreateRequest(url);
                using (cancellation.Register(request.Abort))
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }, cancellation);
        }

        internal static Task DownloadFileAsync(string url, string destination, string label, ProgressReporter progress, CancellationToken cancellation)
        {
            return Task.Run(() =>
            {
                HttpWebRequest request = CreateRequest(url);
                using (cancellation.Register(request.Abort))
                using (WebResponse response = request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = response.ContentLength, received = 0;
                    byte[] buffer = new byte[81920];
                    int read, lastPercent = -2;
                    DateTime lastReport = DateTime.MinValue;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        received += read;
                        int percent = total > 0 ? (int)(received * 100 / total) : -1;
                        if (percent != lastPercent && (DateTime.UtcNow - lastReport).TotalMilliseconds > 150)
                        {
                            lastPercent = percent;
                            lastReport = DateTime.UtcNow;
                            progress(total > 0
                                ? string.Format("{0}… {1}%", label, percent)
                                : string.Format("{0}… {1:0.0} MB", label, received / 1048576.0), percent);
                        }
                    }
                    if (total > 0 && received != total)
                        throw new IOException("The download of " + Path.GetFileName(url) + " ended early.");
                }
            }, cancellation);
        }
    }
}
