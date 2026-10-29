using System;

namespace MediaDownloader
{
    public static class DownloadUrlValidator
    {
        // Validate the link's shape here; yt-dlp determines site/media support later.
        public static bool IsValid(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            string value = input.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '"' || c == '\\')
                    return false;
            }

            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrEmpty(uri.Host);
        }
    }
}
