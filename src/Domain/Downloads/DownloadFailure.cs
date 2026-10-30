using System;

namespace MediaDownloader
{
    public sealed class DownloadFailure
    {
        public string Message { get; private set; }
        public string Suggestion { get; private set; }
        public string Stage { get; private set; }
        public string TechnicalDetails { get; private set; }
        public static DownloadFailure Create(string stage, string details)
        {
            string text = (details ?? "").ToLowerInvariant();
            string message = "Something unexpected went wrong.";
            string suggestion = "Try again. If the problem continues, copy the technical details for troubleshooting.";
            if (text.Contains("private video")) { message = "This video is private."; suggestion = "Choose a publicly accessible video or check your access to this link."; }
            else if (text.Contains("unavailable") || text.Contains("404") || text.Contains("removed")) { message = "This link is no longer available."; suggestion = "Check the link or choose another video."; }
            else if (text.Contains("age") || text.Contains("sign in") || text.Contains("403") || text.Contains("login")) { message = "This video requires access permission."; suggestion = "Check whether the site requires an account or restricts this video."; }
            else if (text.Contains("no space") || text.Contains("disk full")) { message = "There is not enough storage space."; suggestion = "Free up space in the download folder, then retry."; }
            else if (text.Contains("permission denied") || text.Contains("access") && text.Contains("denied")) { message = "Stash cannot write to the download folder."; suggestion = "Choose a folder you can write to, then add the link again."; }
            else if (text.Contains("timed out") || text.Contains("network") || text.Contains("connection") || text.Contains("resolve")) { message = "The internet connection was interrupted."; suggestion = "Check your connection, then retry."; }
            else if (stage == "Merging") { message = "Stash could not finish processing the file."; suggestion = "Check free storage space and update the engines, then retry."; }
            return new DownloadFailure { Message = message, Suggestion = suggestion, Stage = stage, TechnicalDetails = details ?? "No additional details were provided." };
        }
    }
}
