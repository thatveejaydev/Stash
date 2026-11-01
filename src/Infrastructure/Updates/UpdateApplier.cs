using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MediaDownloader
{
    public static class UpdateApplier
    {
        public static bool TryRun(string[] args)
        {
            if (args == null || args.Length != 4 || args[0] != "--apply-update") return false;
            int parentPid;
            if (!int.TryParse(args[3], out parentPid)) return true;
            Apply(args[1], args[2], parentPid);
            return true;
        }

        private static void Apply(string source, string target, int parentPid)
        {
            try
            {
                try
                {
                    using (Process parent = Process.GetProcessById(parentPid))
                        if (!parent.WaitForExit(30000)) throw new TimeoutException("Stash did not exit in time. Close it and try again.");
                }
                catch (ArgumentException) { }
                ReplaceAndLaunch(source, target, path =>
                {
                    using (Process process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) }))
                    {
                        if (process == null || process.WaitForExit(1500))
                            throw new InvalidOperationException("The updated application exited during startup.");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stash could not apply the update.\n\n" + ex.Message,
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static void ReplaceAndLaunch(string source, string target, Action<string> launch)
        {
            string candidate = target + ".update-" + Guid.NewGuid().ToString("N");
            string backup = candidate + ".backup";
            try
            {
                // Copy beside the target first: AppData and the portable EXE can be on different drives.
                File.Copy(source, candidate, false);
                File.Replace(candidate, target, backup);
                try { launch(target); }
                catch (Exception launchError)
                {
                    try { File.Replace(backup, target, null); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Startup failed and the previous executable could not be restored. The backup is at " + backup,
                            new AggregateException(launchError, restoreError));
                    }
                    throw new IOException("The updated application could not start. The previous version was restored.", launchError);
                }
                // Cleanup failures must never roll back an already running update.
                try { File.Delete(backup); } catch { }
                try { File.Delete(source); } catch { }
            }
            finally { try { if (File.Exists(candidate)) File.Delete(candidate); } catch { } }
        }
    }
}
