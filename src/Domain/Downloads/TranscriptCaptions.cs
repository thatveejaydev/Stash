using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Stash
{
    internal static class TranscriptCaptions
    {
        internal sealed class Source
        {
            public string Title { get; set; }
            public string Id { get; set; }
            public string Uploader { get; set; }
            public string Channel { get; set; }
            public string Language { get; set; }
            public Dictionary<string, object> Subtitles { get; set; }
            public Dictionary<string, object> Automatic_Captions { get; set; }
        }

        internal sealed class Track
        {
            public string Language { get; private set; }
            public bool Automatic { get; private set; }

            public Track(string language, bool automatic)
            {
                Language = language;
                Automatic = automatic;
            }
        }

        internal static bool IsLanguageCode(string value)
        {
            return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, @"\A[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8}){0,4}\z", RegexOptions.CultureInvariant) &&
                !string.Equals(value, "all", StringComparison.OrdinalIgnoreCase);
        }

        internal static Source Parse(string json)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Source>(json);
        }

        internal static Track Select(Source source, string requestedLanguage)
        {
            string[] manual = GetLanguages(source.Subtitles);
            string[] automatic = GetLanguages(source.Automatic_Captions);
            if (!string.IsNullOrEmpty(requestedLanguage))
                return SelectLanguage(manual, automatic, requestedLanguage);

            // YouTube marks the original auto-caption track with -orig. Prefer that
            // marker to the base-language auto track, which may be a translation.
            string original = automatic.FirstOrDefault(IsOriginalTrack);
            if (original != null)
            {
                string language = WithoutOriginalSuffix(original);
                string edited = FindLanguage(manual, language);
                return edited == null ? new Track(original, true) : new Track(edited, false);
            }

            if (IsLanguageCode(source.Language))
                return SelectLanguage(manual, automatic, source.Language);

            // Some extractors expose a single language without labeling the audio.
            // Avoid choosing between unrelated languages when there is no source hint.
            if (manual.Length == 1) return new Track(manual[0], false);
            return manual.Length == 0 && automatic.Length == 1 ? new Track(automatic[0], true) : null;
        }

        internal static List<Track> ListTracks(Source source)
        {
            List<Track> tracks = new List<Track>();
            HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string code in GetLanguages(source.Subtitles))
                if (languages.Add(WithoutOriginalSuffix(code))) tracks.Add(new Track(code, false));
            foreach (string code in GetLanguages(source.Automatic_Captions).OrderByDescending(IsOriginalTrack))
                if (languages.Add(WithoutOriginalSuffix(code))) tracks.Add(new Track(code, true));
            return tracks;
        }

        private static string[] GetLanguages(Dictionary<string, object> tracks)
        {
            if (tracks == null) return new string[0];
            return tracks.Where(entry => IsLanguageCode(entry.Key) && entry.Value is IList && ((IList)entry.Value).Count > 0)
                .Select(entry => entry.Key).OrderBy(language => language, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static Track SelectLanguage(string[] manual, string[] automatic, string language)
        {
            string edited = FindLanguage(manual, language);
            if (edited != null) return new Track(edited, false);
            string generated = FindLanguage(automatic, language);
            return generated == null ? null : new Track(generated, true);
        }

        private static string FindLanguage(string[] tracks, string language)
        {
            string exact = tracks.FirstOrDefault(track => string.Equals(track, language, StringComparison.OrdinalIgnoreCase));
            if (exact != null && IsOriginalTrack(language)) return exact;
            string original = tracks.FirstOrDefault(track => IsOriginalTrack(track) &&
                string.Equals(WithoutOriginalSuffix(track), language, StringComparison.OrdinalIgnoreCase));
            if (original != null) return original;
            if (exact != null) return exact;
            string variant = tracks.FirstOrDefault(track => track.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
            if (variant != null) return variant;
            int separator = language.IndexOf('-');
            if (separator < 0) return null;
            string baseLanguage = language.Substring(0, separator);
            return tracks.FirstOrDefault(track => string.Equals(WithoutOriginalSuffix(track), baseLanguage, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOriginalTrack(string language)
        {
            return language.EndsWith("-orig", StringComparison.OrdinalIgnoreCase);
        }

        internal static string WithoutOriginalSuffix(string language)
        {
            return IsOriginalTrack(language) ? language.Substring(0, language.Length - 5) : language;
        }
    }
}
