using System;
using System.Threading;

namespace MediaDownloader
{
    // Coalesce before posting to the UI. Other output keeps its original order.
    internal sealed class ProgressOutputBuffer : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<string, bool> output;
        private readonly Timer timer;
        private string latest;
        private bool latestError, disposed;

        public ProgressOutputBuffer(Action<string, bool> output)
        {
            this.output = output;
            timer = new Timer(_ => { lock (gate) { if (!disposed) Flush(); } }, null, 200, 200);
        }

        public void Add(string line, bool error)
        {
            lock (gate)
            {
                if (disposed) return;
                if (line.StartsWith("STASH_PROGRESS:", StringComparison.Ordinal) ||
                    line.StartsWith("[download] ", StringComparison.Ordinal) && line.Contains("% of"))
                {
                    latest = line;
                    latestError = error;
                }
                else { Flush(); output(line, error); }
            }
        }

        private void Flush()
        {
            if (latest == null) return;
            string line = latest;
            latest = null;
            output(line, latestError);
        }

        public void Dispose()
        {
            lock (gate) { if (disposed) return; Flush(); disposed = true; timer.Dispose(); }
        }
    }
}
