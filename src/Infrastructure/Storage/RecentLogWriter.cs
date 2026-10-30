using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MediaDownloader
{
    // Five files including the active log, each at most 1 MiB.
    public sealed class RecentLogWriter : IDisposable
    {
        private readonly object gate = new object();
        private readonly string directory;
        private StreamWriter writer;
        private long bytes;
        private readonly Queue<string> pending = new Queue<string>();
        private readonly Queue<string> recent = new Queue<string>();
        private readonly Task worker;
        private bool stopping;
        public RecentLogWriter(string directory)
        {
            this.directory = directory;
            worker = Task.Factory.StartNew(Drain, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public void Write(string message)
        {
            lock (gate)
            {
                if (stopping) return;
                if (recent.Count >= 64) recent.Dequeue();
                recent.Enqueue(FormatLine(message));
            }
        }

        private static string FormatLine(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
            if (line.Length > 16000) line = line.Substring(0, 16000) + " [truncated]";
            return line;
        }

        public void WriteError(string message)
        {
            lock (gate)
            {
                if (stopping) return;
                foreach (string line in recent) Enqueue(line);
                recent.Clear();
                Enqueue(FormatLine("ERROR: " + message));
                Monitor.Pulse(gate);
            }
        }

        private void Enqueue(string line)
        {
            if (pending.Count >= 1024) pending.Dequeue();
            pending.Enqueue(line);
        }

        private void Drain()
        {
            while (true)
            {
                string[] batch;
                lock (gate)
                {
                    if (!stopping && pending.Count < 128) Monitor.Wait(gate, 250);
                    if (stopping && pending.Count == 0) break;
                    batch = pending.ToArray();
                    pending.Clear();
                }
                try
                {
                    foreach (string line in batch)
                    {
                    int size = Encoding.UTF8.GetByteCount(line + Environment.NewLine);
                    if (writer == null || bytes + size > 1024 * 1024) OpenLog();
                    writer.WriteLine(line);
                    bytes += size;
                    }
                    if (writer != null) writer.Flush();
                }
                catch (IOException) { Close(); }
                catch (UnauthorizedAccessException) { Close(); }
            }
            try { Close(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
        public void Dispose()
        {
            lock (gate) { stopping = true; Monitor.PulseAll(gate); }
            worker.GetAwaiter().GetResult();
        }
    }
}
