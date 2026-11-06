using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace MediaDownloader
{
    internal static class ThumbnailPreview
    {
        private const int MaximumBytes = 4 * 1024 * 1024;

        internal static Task<Bitmap> LoadAsync(Uri uri, string ffmpegPath, CancellationToken cancellation)
        {
            return Task.Run(async () =>
            {
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(8000);
                    try
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        using (MemoryStream source = new MemoryStream())
                        {
                            if (!TryReadImage(uri, source, timeout.Token)) return null;
                            source.Position = 0;
                            return await DecodeAsync(source, ffmpegPath, timeout.Token).ConfigureAwait(false);
                        }
                    }
                    catch (WebException) { return null; }
                    catch (IOException) { return null; }
                    catch (Win32Exception) { return null; }
                    catch (InvalidOperationException) { return null; }
                    catch (ArgumentException) { return null; }
                    catch (OperationCanceledException) { return null; }
                }
            });
        }

        private static bool TryReadImage(Uri uri, MemoryStream image, CancellationToken cancellation)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent = "Mozilla/5.0";
            using (cancellation.Register(request.Abort))
            using (WebResponse response = request.GetResponse())
            using (Stream input = response.GetResponseStream())
            {
                if (response.ContentLength > MaximumBytes) return false;
                byte[] buffer = new byte[8192];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (image.Length + read > MaximumBytes) return false;
                    image.Write(buffer, 0, read);
                }
                return image.Length > 0;
            }
        }

        private static async Task<Bitmap> DecodeAsync(Stream source, string ffmpegPath, CancellationToken cancellation)
        {
            // Decode only image formats and keep the cached preview small, including for WebP.
            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath,
                "-v quiet -protocol_whitelist pipe -format_whitelist jpeg_pipe,png_pipe,webp_pipe,gif " +
                "-max_pixels 16777216 -i pipe:0 -frames:v 1 " +
                "-vf \"scale=192:108:force_original_aspect_ratio=decrease,pad=192:108:(ow-iw)/2:(oh-ih)/2\" " +
                "-c:v bmp -f image2pipe pipe:1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true
            };
            using (Process process = Process.Start(startInfo))
            using (cancellation.Register(() =>
            {
                try { process.Kill(); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }))
            using (MemoryStream decoded = new MemoryStream())
            {
                await Task.WhenAll(WriteInputAsync(source, process.StandardInput.BaseStream),
                    process.StandardOutput.BaseStream.CopyToAsync(decoded)).ConfigureAwait(false);
                process.WaitForExit();
                if (process.ExitCode != 0 || decoded.Length == 0 || cancellation.IsCancellationRequested) return null;
                decoded.Position = 0;
                using (Image image = Image.FromStream(decoded)) return new Bitmap(image);
            }
        }

        private static async Task WriteInputAsync(Stream source, Stream input)
        {
            using (input)
            {
                try { await source.CopyToAsync(input).ConfigureAwait(false); }
                catch (IOException) { } // The decoder may close its input after the first frame.
            }
        }
    }
}
