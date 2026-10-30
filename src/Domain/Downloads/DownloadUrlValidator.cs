using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MediaDownloader
{
    public static class DownloadUrlValidator
    {
        // Extract all valid HTTP/HTTPS URLs from any text (single link, list of links, or paragraph)
        public static List<string> ExtractUrls(string input)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(input)) return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            MatchCollection matches = Regex.Matches(input, @"https?://[^\s""'<>`]+", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                string url = m.Value;
                if (string.IsNullOrWhiteSpace(url)) continue;

                // Trim trailing punctuation commonly attached in text/markdown/sentences
                url = url.TrimEnd('.', ',', ';', ':', '!', '?', '"', '\'', '<', '>');

                if (url.EndsWith(")") && !url.Contains("(")) url = url.Substring(0, url.Length - 1);
                if (url.EndsWith("]") && !url.Contains("[")) url = url.Substring(0, url.Length - 1);
                if (url.EndsWith("}") && !url.Contains("{")) url = url.Substring(0, url.Length - 1);

                Uri uri;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                    && !string.IsNullOrEmpty(uri.Host)
                    && (uri.Host.Contains(".") || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
                {
                    string finalUrl = uri.AbsoluteUri;
                    if (!seen.Contains(finalUrl))
                    {
                        seen.Add(finalUrl);
                        result.Add(finalUrl);
                    }
                }
            }

            return result;
        }

        // Checks if a URL represents a playlist, album, or channel listing
        public static bool IsPlaylistUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.ToLowerInvariant();
            return u.Contains("list=")
                || u.Contains("/playlist")
                || u.Contains("/sets/")
                || u.Contains("/album/")
                || u.Contains("/videos")
                || u.Contains("/shorts");
        }

        // Extracts single video URL from a URL that contains both a video and a playlist parameter
        public static bool TryExtractSingleVideoUrl(string url, out string singleUrl, out string videoId)
        {
            singleUrl = null;
            videoId = null;
            if (string.IsNullOrWhiteSpace(url)) return false;

            Match m = Regex.Match(url, @"[\?&]v=([a-zA-Z0-9_\-]{11})");
            if (m.Success)
            {
                videoId = m.Groups[1].Value;
                singleUrl = "https://www.youtube.com/watch?v=" + videoId;
                return true;
            }

            try
            {
                Uri uri;
                if (Uri.TryCreate(url, UriKind.Absolute, out uri))
                {
                    if (string.Equals(uri.Host, "youtu.be", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(uri.Host, "www.youtu.be", StringComparison.OrdinalIgnoreCase))
                    {
                        string seg = uri.AbsolutePath.Trim('/');
                        if (seg.Length >= 11)
                        {
                            videoId = seg.Substring(0, 11);
                            singleUrl = "https://www.youtube.com/watch?v=" + videoId;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
