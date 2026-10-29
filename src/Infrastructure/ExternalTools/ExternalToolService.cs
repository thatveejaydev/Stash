using System;
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
            string localBin = Path.Combine(binFolder, exeName);
            if (File.Exists(localBin)) return localBin;

            string localRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
            if (File.Exists(localRoot)) return localRoot;

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        string candidate = Path.Combine(dir.Trim(), exeName);
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch { }
            }

            return localBin;
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

        public static async Task<Tuple<string, string>> FetchMetadataTitleAndIdAsync(string ytDlpPath, DownloadQueueItem item, Action onTitleUpdated)
        {
            string fetchedTitle = "";
            string fetchedId = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = string.Format("--no-playlist --get-title --get-id --no-warnings \"{0}\"", item.Url.Replace("\"", "")),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(psi))
                {
                    Task<string> readAllTask = p.StandardOutput.ReadToEndAsync();
                    Task completed = await Task.WhenAny(readAllTask, Task.Delay(9000));
                    if (completed == readAllTask)
                    {
                        string raw = await readAllTask;
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            string[] lines = raw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length >= 1)
                            {
                                fetchedTitle = lines[0].Trim();
                                item.Title = fetchedTitle;
                                if (onTitleUpdated != null) onTitleUpdated();
                            }
                            if (lines.Length >= 2)
                            {
                                fetchedId = lines[1].Trim();
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
            return Tuple.Create(fetchedTitle, fetchedId);
        }
    }
}
