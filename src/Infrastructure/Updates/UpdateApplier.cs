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
            string backup = target + ".update-backup";
            try
            {
                try { using (Process parent = Process.GetProcessById(parentPid)) parent.WaitForExit(30000); }
                catch (ArgumentException) { }
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(target, backup);
                try
                {
                    File.Move(source, target);
                    Process process = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) });
                    Thread.Sleep(1500);
                    if (process != null && process.HasExited) throw new InvalidOperationException("The updated application exited during startup.");
                    File.Delete(backup);
                }
                catch
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (File.Exists(backup)) File.Move(backup, target);
                    throw;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Stash could not apply the update. The previous version was restored.\n\n" + ex.Message,
                    "Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
