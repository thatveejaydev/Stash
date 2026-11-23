using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Stash
{
    internal static class TranscriptText
    {
        private const string Timestamp = @"[0-9]{2,6}:[0-5][0-9]:[0-5][0-9][,.][0-9]{3}";
        private static readonly Regex Timing = new Regex(@"^(?<start>" + Timestamp + @")\s+-->\s+(?<end>" + Timestamp + @")(?:\s|$)");
        private static readonly Regex Formatting = new Regex(
            @"</?(?:b|i|u|s|font|span)(?:\s+[^<>]*)?>|\{\\an[1-9]\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s+");
        private const string SentenceEndings = ".!?。！？";

        private sealed class Cue
        {
            public string Timestamp;
            public long Start;
            public long End;
            public readonly List<string> Lines = new List<string>();
        }

        internal static void Write(string captionPath, string textPath, bool timed, bool automatic, CancellationToken token)
        {
            Cue previous = null;
            StringBuilder paragraph = new StringBuilder();
            bool wroteText = false;
            using (StreamWriter writer = new StreamWriter(textPath, false, new UTF8Encoding(false)))
            {
                foreach (Cue cue in ReadCues(captionPath, token))
                {
                    int repeatedLines = automatic ? CountRepeatedLines(previous, cue) : 0;
                    int count = cue.Lines.Count - repeatedLines;
                    if (count > 0)
                    {
                        string text = string.Join(" ", cue.Lines.GetRange(repeatedLines, count));
                        if (timed) writer.WriteLine("[{0}] {1}", cue.Timestamp, text);
                        else
                        {
                            if (paragraph.Length > 0 && (cue.Start - previous.End >= 2000 ||
                                paragraph.Length >= 1000 ||
                                paragraph.Length >= 500 && SentenceEndings.IndexOf(paragraph[paragraph.Length - 1]) >= 0))
                            {
                                writer.WriteLine(paragraph);
                                writer.WriteLine();
                                paragraph.Clear();
                            }
                            if (paragraph.Length > 0) paragraph.Append(' ');
                            paragraph.Append(text);
                        }
                        wroteText = true;
                    }
                    previous = cue;
                }
                if (paragraph.Length > 0) writer.WriteLine(paragraph);
            }
            if (!wroteText) throw new InvalidDataException("The captions did not contain any readable text.");
        }

        private static IEnumerable<Cue> ReadCues(string captionPath, CancellationToken token)
        {
            Cue cue = null;
            foreach (string rawLine in File.ReadLines(captionPath))
            {
                token.ThrowIfCancellationRequested();
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    if (cue != null && cue.Lines.Count > 0) yield return cue;
                    cue = null;
                    continue;
                }
                if (cue == null)
                {
                    Match timing = Timing.Match(line);
                    if (!timing.Success) continue;
                    string start = timing.Groups["start"].Value.Replace(',', '.');
                    cue = new Cue
                    {
                        Timestamp = start,
                        Start = ToMilliseconds(start),
                        End = ToMilliseconds(timing.Groups["end"].Value.Replace(',', '.'))
                    };
                }
                else
                {
                    string text = WebUtility.HtmlDecode(Formatting.Replace(line, ""));
                    text = Whitespace.Replace(text, " ").Trim();
                    if (text.Length > 0) cue.Lines.Add(text);
                }
            }
            if (cue != null && cue.Lines.Count > 0) yield return cue;
        }

        private static long ToMilliseconds(string timestamp)
        {
            string[] parts = timestamp.Split(':', '.');
            return long.Parse(parts[0], CultureInfo.InvariantCulture) * 3600000 +
                int.Parse(parts[1], CultureInfo.InvariantCulture) * 60000 +
                int.Parse(parts[2], CultureInfo.InvariantCulture) * 1000 +
                int.Parse(parts[3], CultureInfo.InvariantCulture);
        }

        private static int CountRepeatedLines(Cue previous, Cue current)
        {
            if (previous == null || current.Start - previous.End > 20) return 0;
            // Rolling captions carry lines forward as new lines appear. Preserve
            // successive one-line cues when their display times don't overlap.
            if (previous.Lines.Count == 1 && current.Lines.Count == 1 && current.Start >= previous.End) return 0;
            for (int overlap = Math.Min(previous.Lines.Count, current.Lines.Count); overlap > 0; overlap--)
            {
                int offset = previous.Lines.Count - overlap;
                int index = 0;
                while (index < overlap && string.Equals(previous.Lines[offset + index], current.Lines[index], StringComparison.Ordinal))
                    index++;
                if (index == overlap) return overlap;
            }
            return 0;
        }
    }
}
