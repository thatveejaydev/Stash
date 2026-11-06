using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MediaDownloader
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
            catch { }
        }

        public static Task DownloadFileAsync(string url, string destinationPath)
        {
            return Task.Run(() =>
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Stash-Windows-Downloader/2.5");
                    wc.DownloadFile(url, destinationPath);
                }
            });
        }

        public static Task<PlaylistInfo> FetchPlaylistInfoAsync(string ytDlpPath, string playlistUrl)
        {
            return FetchPlaylistInfoAsync(ytDlpPath, playlistUrl, CookieBrowser.None);
        }

        internal static async Task<PlaylistInfo> FetchPlaylistInfoAsync(string ytDlpPath, string playlistUrl, CookieBrowser browser)
        {
            PlaylistInfo info = new PlaylistInfo();
            try
            {
                string safeUrl = playlistUrl.Replace("\"", "");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = YtDlpCommandBuilder.BuildBrowserCookieArguments(browser) + string.Format(
                        "--flat-playlist --print \"%(playlist_title)s\t%(playlist_count)s\t%(id)s\t%(title)s\t%(url)s\" --no-warnings \"{0}\"",
                        safeUrl),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(psi))
                {
                    Task<string[]> readTask = Task.WhenAll(p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
                    Task completed = await Task.WhenAny(readTask, Task.Delay(15000));
                    if (completed == readTask)
                    {
                        string[] output = await readTask;
                        string raw = output[0];
                        info.CookieAccessFailed = DownloadFailure.IsBrowserCookieError(output[1]);
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                string[] parts = line.Split('\t');
                                if (parts.Length >= 4)
                                {
                                    if (string.IsNullOrEmpty(info.Title) && parts[0] != "NA" && !string.IsNullOrWhiteSpace(parts[0]))
                                    {
                                        info.Title = StorageFileService.SanitizeFileName(parts[0].Trim());
                                    }

                                    if (info.Count == 0 && parts[1] != "NA")
                                    {
                                        int c;
                                        if (int.TryParse(parts[1].Trim(), out c))
                                        {
                                            info.Count = c;
                                        }
                                    }

                                    string entryId = parts[2].Trim();
                                    string entryTitle = StorageFileService.SanitizeFileName(parts[3].Trim());
                                    string entryUrl = parts.Length >= 5 ? parts[4].Trim() : "";
                                    if (string.IsNullOrEmpty(entryUrl) || entryUrl == "NA")
                                    {
                                        entryUrl = "https://www.youtube.com/watch?v=" + entryId;
                                    }

                                    if (!string.IsNullOrEmpty(entryId))
                                    {
                                        PlaylistEntry entry = new PlaylistEntry
                                        {
                                            Id = entryId,
                                            Title = string.IsNullOrEmpty(entryTitle) ? entryId : entryTitle,
                                            Url = entryUrl
                                        };
                                        info.Entries.Add(entry);
                                    }
                                }
                            }

                            if (info.Count < info.Entries.Count)
                            {
                                info.Count = info.Entries.Count;
                            }
                        }
                    }
                    else
                    {
                        KillProcessTree(p.Id);
                    }
                }
            }
            catch { }

            return info;
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
        public string Title { get; set; }
        public int Count { get; set; }
        public List<PlaylistEntry> Entries { get; set; }

        public PlaylistInfo()
        {
            Entries = new List<PlaylistEntry>();
        }
    }
}
