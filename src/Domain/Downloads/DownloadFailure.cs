using System;
using System.Text.RegularExpressions;

namespace Stash
{
    public sealed class DownloadFailure
    {
        public string Message { get; private set; }
        public string Suggestion { get; private set; }
        public string Stage { get; private set; }
        public string TechnicalDetails { get; private set; }

        private const string CookieSuggestion = "If it plays when signed in, choose that browser from the cookie icon and retry.";

        // Ordered from most to least specific. Patterns match yt-dlp's own wording with word
        // boundaries, so ordinary words such as "webpage" or "message" do not trigger a rule.
        private static readonly Rule[] Rules =
        {
            new Rule(@"^No captions available in this language\.$",
                "No captions available in this language.",
                "Check which captions the source provides, then add the link with a different language."),
            new Rule(@"The captions did not contain any readable text\.",
                "The captions did not contain any readable text.",
                "Try a different caption language or save the original captions as SRT or VTT."),
            new Rule(@"\bprivate video\b|\bvideo is private\b",
                "This video is private.", CookieSuggestion),
            new Rule(@"requested format is not available|no video formats found",
                "The selected format or quality isn't available for this video.",
                "Choose Best quality or a different format, then retry."),
            new Rule(@"\bunsupported url\b",
                "This website or link isn't supported.",
                "Check that the link opens a video or audio page, then try again."),
            new Rule(@"http error 429|too many requests|rate[- ]limit",
                "The site is temporarily limiting downloads.",
                "Wait a few minutes, lower Parallel downloads, then retry."),
            new Rule(@"sign in to confirm|confirm (that )?you.?re not a bot|\bage[- ]restricted\b|inappropriate for some users|" +
                     @"\blogin required\b|\blog in\b|requires? (authentication|login|sign[- ]in)|members[- ]only|join this channel|" +
                     @"premium members|http error 403|\b403\b:? forbidden|\bpaid\b.*\bcontent\b|\bsubscriber[- ]only\b",
                "This video requires access permission.", CookieSuggestion),
            new Rule(@"\bvideo unavailable\b|this video is (no longer|not) available|has been removed|\bremoved by\b|" +
                     @"no longer available|http error 404|\b404\b:? not found|content isn.?t available|does not exist",
                "This link is no longer available.", "Check the link or choose another video."),
            new Rule(@"no space left|disk (is )?full|not enough (disk )?space|errno 28\b",
                "There is not enough storage space.", "Free up space in the download folder, then retry."),
            new Rule(@"permission denied|access (is )?denied|errno 13\b|read-only file system",
                "Stash cannot write to the download folder.", "Choose a folder you can write to, then add the link again."),
            new Rule(@"timed out|\btimeout\b|network is unreachable|connection (was )?(reset|refused|aborted|closed)|" +
                     @"getaddrinfo failed|failed to resolve|name resolution|unable to download (webpage|api page|json)|" +
                     @"\bssl\b|certificate verify failed|remote end closed|incompleteread|urlopen error",
                "The internet connection was interrupted.", "Check your connection, then retry.")
        };

        private sealed class Rule
        {
            public readonly Regex Pattern;
            public readonly string Message;
            public readonly string Suggestion;

            public Rule(string pattern, string message, string suggestion)
            {
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Message = message;
                Suggestion = suggestion;
            }
        }

        internal static bool IsBrowserCookieError(string details)
        {
            string text = (details ?? "").ToLowerInvariant();
            return text.Contains("failed to decrypt with dpapi") ||
                text.Contains("cookie database") && (text.Contains("could not") || text.Contains("failed")) ||
                text.Contains("failed to decrypt cookies") ||
                text.Contains("could not copy") && text.Contains("cookie");
        }

        public static DownloadFailure Create(string stage, string details)
        {
            string text = details ?? "";
            string message = "Something unexpected went wrong.";
            string suggestion = "Try again. If the problem continues, copy the technical details for troubleshooting.";
            if (IsBrowserCookieError(text))
            {
                message = "Could not read browser cookies.";
                suggestion = "Open Browser Cookies using the cookie icon, choose a browser where you're signed in, then retry. Firefox is recommended on Windows.";
            }
            else
            {
                Rule match = null;
                foreach (Rule rule in Rules)
                    if (rule.Pattern.IsMatch(text)) { match = rule; break; }
                if (match != null) { message = match.Message; suggestion = match.Suggestion; }
                else if (stage == "Merging") { message = "Stash could not finish processing the file."; suggestion = "Check free storage space and update the engines, then retry."; }
            }
            return new DownloadFailure
            {
                Message = message,
                Suggestion = suggestion,
                Stage = stage,
                TechnicalDetails = string.IsNullOrWhiteSpace(details) ? "No additional details were provided." : details
            };
        }
    }
}
