using System;
using System.IO;
using System.Text;

namespace MediaDownloader
{
    // Five files including the active log, each at most 1 MiB.
    public sealed class RecentLogWriter : IDisposable
    {
        private readonly object gate = new object();
        private readonly string directory;
        private StreamWriter writer;
        private long bytes;
        public RecentLogWriter(string directory) { this.directory = directory; }
        public void Write(string message)
        {
            lock (gate)
            {
                try
                {
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
                    if (line.Length > 16000) line = line.Substring(0, 16000) + " [truncated]";
                    int size = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
                    if (writer == null || bytes + size > 1024 * 1024) OpenLog();
                    writer.WriteLine(line);
                    writer.Flush();
                    bytes += size;
                }
                catch (IOException) { Close(); }
                catch (UnauthorizedAccessException) { Close(); }
            }
        }
        private void OpenLog()
        {
            Close();
            Directory.CreateDirectory(directory);
            FileInfo[] logs = new DirectoryInfo(directory).GetFiles("stash-*.log");
            Array.Sort(logs, (a, b) => b.CreationTimeUtc.CompareTo(a.CreationTimeUtc));
            for (int i = 4; i < logs.Length; i++) logs[i].Delete();
            string path = Path.Combine(directory, "stash-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".log");
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
            bytes = 0;
        }
        private void Close() { if (writer != null) { writer.Dispose(); writer = null; } }
        public void Dispose() { lock (gate) Close(); }
    }
}
