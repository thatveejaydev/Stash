using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stash
{
    public static class ExternalToolService
    {
        public static string FindExecutable(string binFolder, string exeName)
        {
            return Path.Combine(binFolder, exeName);
        }

        public static void KillProcessTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/F /T /PID " + pid)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(2000);
                }
            }
            catch (Exception ex) { AppLog.Error("taskkill failed for process " + pid, ex); }
        }

        public static Task<PlaylistInfo> FetchPlaylistInfoAsync(string ytDlpPath, string playlistUrl)
        {
            return FetchPlaylistInfoAsync(ytDlpPath, playlistUrl, CookieBrowser.None, TimeSpan.FromMinutes(3), CancellationToken.None);
        }

        internal static async Task<PlaylistInfo> FetchPlaylistInfoAsync(string ytDlpPath, string playlistUrl, CookieBrowser browser,
            TimeSpan timeout, CancellationToken cancellation)
        {
            PlaylistInfo info = new PlaylistInfo();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = YtDlpCommandBuilder.BuildBrowserCookieArguments(browser) +
                        "--flat-playlist --print \"%(playlist_title)s\t%(playlist_count)s\t%(id)s\t%(title)s\t%(url)s\" --no-warnings " +
                        CommandLine.Quote(playlistUrl),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(psi))
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    limit.CancelAfter(timeout);
                    Task<string[]> readTask = Task.WhenAll(p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
                    TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>();
                    using (limit.Token.Register(() => stopped.TrySetResult(true)))
                    {
                        Task completed = await Task.WhenAny(readTask, stopped.Task);
                        if (completed != readTask)
                        {
                            KillProcessTree(p.Id);
                            info.TimedOut = !cancellation.IsCancellationRequested;
                            return info;
                        }
                    }
                    string[] output = await readTask;
                    info.CookieAccessFailed = DownloadFailure.IsBrowserCookieError(output[1]);
                    if (!await Task.Run(() => p.WaitForExit(5000)))
                    {
                        KillProcessTree(p.Id);
                        return info;
                    }
                    ParsePlaylistOutput(output[0], info);
                    // yt-dlp exits non-zero when some entries are unavailable; keep what it listed.
                    if (p.ExitCode != 0 && info.Entries.Count == 0) info.ErrorDetails = output[1];
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("Playlist lookup failed", ex);
                info.ErrorDetails = ex.Message;
            }

            return info;
        }

        internal static void ParsePlaylistOutput(string raw, PlaylistInfo info)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 4) continue;

                if (string.IsNullOrEmpty(info.Title) && parts[0] != "NA" && !string.IsNullOrWhiteSpace(parts[0]))
                    info.Title = StorageFileService.SanitizeFileName(parts[0].Trim());

                int reported;
                if (info.ReportedCount == 0 && parts[1] != "NA" && int.TryParse(parts[1].Trim(), out reported))
                    info.ReportedCount = reported;

                string entryId = parts[2].Trim();
                if (string.IsNullOrEmpty(entryId) || entryId == "NA" || !seen.Add(entryId)) continue;
                string entryTitle = StorageFileService.SanitizeFileName(parts[3].Trim());
                string entryUrl = parts.Length >= 5 ? parts[4].Trim() : "";
                Uri parsed;
                if (string.IsNullOrEmpty(entryUrl) || entryUrl == "NA" || !Uri.TryCreate(entryUrl, UriKind.Absolute, out parsed) ||
                    (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    entryUrl = "https://www.youtube.com/watch?v=" + entryId;
                }

                info.Entries.Add(new PlaylistEntry
                {
                    Id = entryId,
                    Title = string.IsNullOrEmpty(entryTitle) || entryTitle == "NA" ? entryId : entryTitle,
                    Url = entryUrl
                });
            }
        }
    }

    public class PlaylistEntry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
    }

    public class PlaylistInfo
    {
        internal bool CookieAccessFailed { get; set; }
        internal bool TimedOut { get; set; }
        internal string ErrorDetails { get; set; }
        public string Title { get; set; }
        // The size reported by the site; it can include private or deleted entries that are not listed.
        public int ReportedCount { get; set; }
        public int Count { get { return Entries.Count; } }
        public int UnavailableCount { get { return Math.Max(0, ReportedCount - Entries.Count); } }
        public List<PlaylistEntry> Entries { get; set; }

        public PlaylistInfo()
        {
            Entries = new List<PlaylistEntry>();
        }
    }
}
