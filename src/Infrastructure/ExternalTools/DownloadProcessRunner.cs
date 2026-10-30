using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MediaDownloader
{
    public interface IDownloadProcessRunner
    {
        Task<int> RunAsync(string executable, string arguments, CancellationToken cancellationToken,
            Action<string> onOutput, Action<string> onError);
    }

    // Owns each process from startup until exit. Callers only request cancellation;
    // they never inspect or dispose a process that another thread is still using.
    public sealed class DownloadProcessRunner : IDownloadProcessRunner
    {
        public Task<int> RunAsync(string executable, string arguments, CancellationToken cancellationToken,
            Action<string> onOutput, Action<string> onError)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null && onOutput != null) onOutput(e.Data);
                    };
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null && onError != null) onError(e.Data);
                    };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    // Only this worker accesses the process, including cancellation.
                    // A UI command sets the token and never waits for taskkill itself.
                    while (!process.WaitForExit(150))
                    {
                        if (!cancellationToken.IsCancellationRequested) continue;
                        ExternalToolService.KillProcessTree(process.Id);
                        // taskkill may be unavailable or fail. The runner still owns
                        // the started process and can terminate it directly.
                        try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { } // Process already exited.
                        break;
                    }
                    // Also drains output callbacks before disposal or a subsequent resume.
                    process.WaitForExit();
                    cancellationToken.ThrowIfCancellationRequested();
                    return process.ExitCode;
                }
            });
        }
    }
}
