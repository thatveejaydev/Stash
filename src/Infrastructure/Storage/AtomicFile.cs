using System;
using System.IO;
using System.Text;

namespace Stash
{
    // Writes to a sibling temporary file first, so a crash never leaves a truncated file behind.
    internal static class AtomicFile
    {
        internal static void WriteAllText(string path, string contents)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null, true);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    // Process-wide diagnostics sink. The main window connects it to the rotating log writer.
    internal static class AppLog
    {
        private static Action<string> error;

        internal static void Connect(Action<string> errorSink) { error = errorSink; }

        internal static void Error(string context, Exception exception)
        {
            Error(context + ": " + (exception == null ? "unknown error" : exception.ToString()));
        }

        internal static void Error(string message)
        {
            Action<string> sink = error;
            if (sink == null) return;
            try { sink(message); }
            catch (ObjectDisposedException) { }
        }
    }
}
