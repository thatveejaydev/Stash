using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
{
    public partial class MainForm : Form
    {
        private void CheckToolsStatus()
        {
            ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
            ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");

            bool hasYtDlp = File.Exists(ytDlpPath);
            bool hasFfmpeg = File.Exists(ffmpegPath);

            if (hasYtDlp && hasFfmpeg)
            {
                lblToolsStatus.Text = "● Engine Ready";
                lblToolsStatus.ForeColor = Theme.AccentSuccess;
                btnInstallTools.Text = "Update Engine";
                btnInstallTools.Size = new Size(Dpi.Px(108), Dpi.Px(27));
                btnInstallTools.BaseColor = Theme.BgTopBar;
                btnInstallTools.HoverColor = Theme.BgSurfaceHover;
                btnInstallTools.BorderColor = Color.Empty;
                btnInstallTools.ForeColor = Theme.TextSecondary;
            }
            else
            {
                List<string> missing = new List<string>();
                if (!hasYtDlp) missing.Add("yt-dlp");
                if (!hasFfmpeg) missing.Add("FFmpeg");
                lblToolsStatus.Text = "● Missing: " + string.Join(" & ", missing);
                lblToolsStatus.ForeColor = Theme.AccentWarning;
                btnInstallTools.Text = "Install yt-dlp + FFmpeg";
                btnInstallTools.Size = new Size(Dpi.Px(165), Dpi.Px(27));
                btnInstallTools.BaseColor = Theme.BrandPurple;
                btnInstallTools.HoverColor = Theme.BrandPurpleHover;
                btnInstallTools.BorderColor = Color.Empty;
                btnInstallTools.ForeColor = Color.White;
            }
        }

        private async void BtnInstallTools_Click(object sender, EventArgs e)
        {
            if (!btnInstallTools.Enabled) return;
            btnInstallTools.Enabled = false;
            try
            {
                if (!Directory.Exists(binFolder)) Directory.CreateDirectory(binFolder);

                string targetYtDlp = Path.Combine(binFolder, "yt-dlp.exe");

                if (!File.Exists(ytDlpPath))
                {
                    AppendLog("Downloading latest yt-dlp.exe from GitHub releases...");
                    lblToolsStatus.Text = "● Downloading yt-dlp.exe...";
                    await ExternalToolService.DownloadFileAsync("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", targetYtDlp);
                    AppendLog("yt-dlp.exe installed to " + targetYtDlp);
                }
                else
                {
                    AppendLog("Checking for yt-dlp updates (-U)...");
                    lblToolsStatus.Text = "● Updating yt-dlp...";
                    await Task.Run(() =>
                    {
                        try
                        {
                            ProcessStartInfo psi = new ProcessStartInfo(ytDlpPath, "-U")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                RedirectStandardOutput = true
                            };
                            using (Process p = Process.Start(psi))
                            {
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit();
                                if (p.ExitCode != 0) logWriter.WriteError("Tool update failed with exit code " + p.ExitCode + ": " + output);
                                if (!string.IsNullOrWhiteSpace(output))
                                    AppendLog(output.Trim());
                            }
                        }
                        catch (Exception ex)
                        {
                            logWriter.WriteError("Update check failed: " + ex.ToString());
                        }
                    });
                }

                ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
                ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");

                if (!File.Exists(ffmpegPath))
                {
                    AppendLog("Downloading FFmpeg static build (yt-dlp/FFmpeg-Builds)...");
                    lblToolsStatus.Text = "● Downloading FFmpeg (~80 MB)...";
                    string zipPath = Path.Combine(binFolder, "ffmpeg.zip");
                    string ffmpegUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
                    await ExternalToolService.DownloadFileAsync(ffmpegUrl, zipPath);

                    AppendLog("Extracting ffmpeg.exe and ffprobe.exe into " + binFolder + "...");
                    lblToolsStatus.Text = "● Extracting FFmpeg...";
                    await Task.Run(() =>
                    {
                        string extractScript = string.Format(
                            "Add-Type -AssemblyName System.IO.Compression.FileSystem; " +
                            "$zip = [System.IO.Compression.ZipFile]::OpenRead('{0}'); " +
                            "foreach ($entry in $zip.Entries) {{ " +
                            "  if ($entry.Name -eq 'ffmpeg.exe' -or $entry.Name -eq 'ffprobe.exe') {{ " +
                            "    $dest = Join-Path '{1}' $entry.Name; " +
                            "    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dest, $true); " +
                            "  }} " +
                            "}} " +
                            "$zip.Dispose(); Remove-Item '{0}' -Force",
                            zipPath.Replace("'", "''"), binFolder.Replace("'", "''"));

                        ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -Command \"" + extractScript + "\"")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (Process p = Process.Start(psi))
                        {
                            p.WaitForExit();
                        }
                    });
                    AppendLog("FFmpeg ready at " + binFolder);
                }

                CheckToolsStatus();
            }
            catch (Exception ex)
            {
                logWriter.WriteError("Error installing tools: " + ex.ToString());
                MessageBox.Show("Failed to download dependencies:\n" + ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CheckToolsStatus();
            }
            finally
            {
                btnInstallTools.Enabled = true;
            }
        }

    }
}
