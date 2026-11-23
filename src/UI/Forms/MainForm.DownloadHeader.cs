using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private static readonly string[] FormatShortNames = { "MP4", "MKV", "MP3", "M4A", "FLAC", "WAV" };
        private static readonly string[] QualityNames = { "Best quality", "4K", "1440p", "1080p", "720p", "480p", "360p" };
        private static readonly string[] TranscriptFormatNames = { "SRT", "VTT", "TXT (Timed)", "TXT (Plain)" };
        private static readonly string[] BatchLanguages = { "en", "zh", "fr", "de", "hi", "ja", "ko", "pt", "es" };

        private CancellationTokenSource captionCheck;
        private Task<TranscriptCaptions.Source> captionTask;
        private TranscriptCaptions.Source captionSource;
        private bool submittingLink;

        private bool IsTranscriptMode { get { return settings.SelectedKind == DownloadKind.Transcript; } }
        private bool DownloadToolsReady { get { return File.Exists(ytDlpPath) && File.Exists(ffmpegPath); } }

        private void UpdateHeaderSettings()
        {
            DownloadKind kind = settings.SelectedKind;
            string first = IsTranscriptMode ? CaptionLanguageName(settings.TranscriptLanguage) : FormatShortNames[settings.FormatIndex];
            string second = kind == DownloadKind.Video ? QualityNames[settings.QualityIndex] :
                IsTranscriptMode ? TranscriptFormatNames[(int)settings.TranscriptFormat] : null;
            linkBar.ShowSettings(kind, first, second, GetFolderShortDisplayName(settings.CurrentOutputFolder));
            appToolTip.SetToolTip(linkBar.FormatAnchor, IsTranscriptMode ? first : null);
            appToolTip.SetToolTip(linkBar.FolderAnchor, settings.CurrentOutputFolder);
        }

        private static string CaptionLanguageName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "Original";
            code = TranscriptCaptions.WithoutOriginalSuffix(code);
            try { return CultureInfo.GetCultureInfo(code).EnglishName; }
            catch (CultureNotFoundException) { return code; }
        }

        private void ShowPrimaryChoice()
        {
            if (IsTranscriptMode)
            {
                ShowCaptionLanguages();
                return;
            }
            using (HeaderChoices choices = new HeaderChoices(linkBar.FormatAnchor))
            {
                bool video = settings.SelectedKind == DownloadKind.Video;
                int first = video ? (int)DownloadMode.BestVideoAndAudioMp4 : (int)DownloadMode.BestAudioOnlyMp3;
                int last = video ? (int)DownloadMode.BestVideoAndAudioMkv : (int)DownloadMode.BestAudioOnlyWav;
                for (int i = first; i <= last; i++)
                {
                    int index = i;
                    choices.Add(FormatShortNames[i], settings.FormatIndex == i, () => settings.FormatIndex = index);
                }
                choices.ShowAndSave(this);
            }
        }

        private void ShowDetailChoice()
        {
            using (HeaderChoices choices = new HeaderChoices(linkBar.DetailAnchor))
            {
                bool transcript = IsTranscriptMode;
                string[] names = transcript ? TranscriptFormatNames : QualityNames;
                int selected = transcript ? (int)settings.TranscriptFormat : settings.QualityIndex;
                for (int i = 0; i < names.Length; i++)
                {
                    int index = i;
                    choices.Add(names[i], selected == i, () =>
                    {
                        if (transcript) settings.TranscriptFormat = (TranscriptFormat)index;
                        else settings.QualityIndex = index;
                    });
                }
                choices.ShowAndSave(this);
            }
        }

        private void ShowCaptionLanguages()
        {
            using (HeaderChoices choices = new HeaderChoices(linkBar.FormatAnchor))
            {
                bool hasOriginal = captionSource == null || TranscriptCaptions.Select(captionSource, null) != null;
                choices.Add(hasOriginal ? "Original" : "Original (unavailable)", settings.TranscriptLanguage == null,
                    () => SetCaptionLanguage(null), hasOriginal);
                if (captionSource != null)
                {
                    foreach (TranscriptCaptions.Track track in TranscriptCaptions.ListTracks(captionSource))
                    {
                        string code = track.Language;
                        string caption = CaptionLanguageName(code) + (track.Automatic ? "  ·  Auto" : "  ·  Creator");
                        choices.Add(caption, string.Equals(code, settings.TranscriptLanguage, StringComparison.OrdinalIgnoreCase),
                            () => SetCaptionLanguage(code));
                    }
                }
                else
                {
                    // A batch can contain unrelated videos, so it uses a language preference per job.
                    foreach (string code in BatchLanguages)
                    {
                        string selectedCode = code;
                        choices.Add(CaptionLanguageName(code), string.Equals(code, settings.TranscriptLanguage, StringComparison.OrdinalIgnoreCase),
                            () => SetCaptionLanguage(selectedCode));
                    }
                }
                choices.ShowAndSave(this);
            }
        }

        private void SetCaptionLanguage(string code)
        {
            settings.TranscriptLanguage = code;
            if (captionSource != null) ShowCaptionAvailability();
        }

        private void ShowFolderPopup(Control anchor)
        {
            ToolStripDropDown popup = null;
            Panel content = BuildSaveToSubmenuPanel(() => popup, anchor);
            popup = BufferedDropDown.Create(content, true);
            popup.Closed += (s, e) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)popup.Dispose);
            };
            popup.Show(anchor, new Point(anchor.Width - popup.Width, anchor.Height + Dpi.Px(5)));
        }

        // The header's format, quality and caption-language menus. They use the same owner-drawn
        // rows and buffered popup as the Downloads folder menu; long lists scroll.
        private sealed class HeaderChoices : IDisposable
        {
            private const int MaxVisibleRows = 10;

            private sealed class Choice
            {
                public string Text;
                public bool Selected;
                public bool Enabled;
                public Action Select;
            }

            private readonly Control anchor;
            private readonly List<Choice> choices = new List<Choice>();

            public HeaderChoices(Control anchor)
            {
                this.anchor = anchor;
            }

            public void Add(string text, bool selected, Action select, bool enabled = true)
            {
                choices.Add(new Choice { Text = text, Selected = selected, Enabled = enabled, Select = select });
            }

            public void ShowAndSave(MainForm form)
            {
                if (choices.Count == 0) return;
                int rowHeight = Dpi.Px(34);
                int pad = Dpi.Px(4);
                int visibleRows = Math.Min(choices.Count, MaxVisibleRows);
                bool scrolls = choices.Count > visibleRows;
                int scrollWidth = scrolls ? Dpi.Px(10) : 0;

                Font measureFont = Theme.GetFont("Segoe UI", 9.2F, FontStyle.Bold);
                int textWidth = 0;
                foreach (Choice choice in choices)
                    textWidth = Math.Max(textWidth, TextRenderer.MeasureText(choice.Text, measureFont).Width);
                int width = Math.Max(anchor.Width, Math.Max(Dpi.Px(160), textWidth + Dpi.Px(48) + pad * 2 + scrollWidth));

                Panel container = Theme.CreatePopupPanel(width, visibleRows * rowHeight + pad * 2);
                Panel viewport = new Panel
                {
                    BackColor = Theme.BgSurface,
                    Location = new Point(pad, pad),
                    Size = new Size(width - pad * 2 - scrollWidth, visibleRows * rowHeight)
                };
                Panel content = new Panel
                {
                    BackColor = Theme.BgSurface,
                    Location = Point.Empty,
                    Size = new Size(viewport.Width, choices.Count * rowHeight)
                };
                viewport.Controls.Add(content);
                container.Controls.Add(viewport);

                DarkScrollBar bar = null;
                if (scrolls)
                {
                    bar = new DarkScrollBar
                    {
                        BackColor = Theme.BgSurface,
                        Location = new Point(viewport.Right, pad),
                        Size = new Size(scrollWidth, viewport.Height),
                        Maximum = content.Height - 1,
                        LargeChange = viewport.Height,
                        SmallChange = rowHeight
                    };
                    bar.Scroll += (s, e) => content.Top = -bar.Value;
                    container.Controls.Add(bar);
                }

                ToolStripDropDown popup = BufferedDropDown.Create(container, true);
                int selectedIndex = -1;
                for (int i = 0; i < choices.Count; i++)
                {
                    Choice choice = choices[i];
                    if (choice.Selected && selectedIndex < 0) selectedIndex = i;
                    PopupMenuRow row = new PopupMenuRow(choice.Text, null, null, choice.Selected)
                    {
                        Location = new Point(0, i * rowHeight),
                        Width = content.Width,
                        TitleOverrideColor = choice.Enabled ? Color.Empty : Theme.TextMuted,
                        Cursor = choice.Enabled ? Cursors.Hand : Cursors.Default
                    };
                    row.MouseDown += (s, e) =>
                    {
                        if (e.Button != MouseButtons.Left || !choice.Enabled) return;
                        popup.Close();
                        choice.Select();
                        form.settings.Save();
                        form.UpdateHeaderSettings();
                    };
                    content.Controls.Add(row);
                }

                // Wheel messages go to the focused window, not the popup, so route them here while it is open.
                WheelRouter wheel = null;
                if (bar != null)
                {
                    if (selectedIndex >= 0) bar.Value = (selectedIndex - visibleRows / 2) * rowHeight;
                    wheel = new WheelRouter(container, bar);
                    Application.AddMessageFilter(wheel);
                }

                popup.Closed += (s, e) =>
                {
                    if (wheel != null) Application.RemoveMessageFilter(wheel);
                    if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke((Action)popup.Dispose);
                };
                popup.Show(anchor, new Point(0, anchor.Height + Dpi.Px(5)));
            }

            public void Dispose() { }
        }

        private sealed class WheelRouter : IMessageFilter
        {
            private const int WmMouseWheel = 0x020A;
            private readonly Control area;
            private readonly DarkScrollBar bar;

            public WheelRouter(Control area, DarkScrollBar bar)
            {
                this.area = area;
                this.bar = bar;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmMouseWheel || area.IsDisposed || !area.IsHandleCreated) return false;
                if (!area.RectangleToScreen(area.ClientRectangle).Contains(Control.MousePosition)) return false;
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                bar.Value -= delta * bar.SmallChange * 3 / 120;
                return true;
            }
        }

        private void CancelCaptionCheck()
        {
            CancellationTokenSource previous = captionCheck;
            captionCheck = null;
            captionTask = null;
            captionSource = null;
            if (previous != null) previous.Cancel();
        }

        private void RefreshCaptionPreview()
        {
            CancelCaptionCheck();
            if (IsDisposed || Disposing) return;
            linkBar.ShowStatus("", Theme.TextMuted);
            List<string> urls = DownloadUrlValidator.ExtractUrls(linkBar.LinkText);
            if (urls.Count > 1)
                linkBar.ShowStatus(urls.Count + " links ready with the current settings.", Theme.TextSecondary);
            if (!IsTranscriptMode || downloadManager == null) return;
            if (urls.Count != 1 || DownloadUrlValidator.IsPlaylistUrl(urls[0])) return;
            if (!DownloadToolsReady || engineOperationRunning)
            {
                linkBar.ShowStatus(engineOperationRunning ? "Preparing download engines…" : "Install the download engines to check captions.", Theme.TextSecondary);
                return;
            }

            CancellationTokenSource check = new CancellationTokenSource();
            captionCheck = check;
            linkBar.ShowStatus("Checking captions…", Theme.TextSecondary);
            captionTask = InspectLinkCaptionsAsync(urls[0], settings.BrowserCookies, check);
        }

        private async Task<TranscriptCaptions.Source> InspectLinkCaptionsAsync(string url, CookieBrowser browser, CancellationTokenSource check)
        {
            try
            {
                await Task.Delay(300, check.Token);
                TranscriptCaptions.Source source = await downloadManager.InspectCaptionsAsync(ytDlpPath, url, browser, check.Token);
                if (captionCheck != check || IsDisposed) return null;
                captionSource = source;
                ShowCaptionAvailability();
                return source;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                if (captionCheck == check && !IsDisposed)
                {
                    DownloadFailure failure = DownloadFailure.Create("Finding captions", ex.Message);
                    string message = failure.Message == "Something unexpected went wrong." ?
                        "Couldn’t check captions. Press Enter to try again." : failure.Message;
                    linkBar.ShowStatus(message, Theme.AccentWarning);
                }
                return null;
            }
            finally
            {
                if (captionCheck == check) captionCheck = null;
                check.Dispose();
            }
        }

        private bool ShowCaptionAvailability()
        {
            TranscriptCaptions.Track track = TranscriptCaptions.Select(captionSource, settings.TranscriptLanguage);
            if (track != null)
            {
                linkBar.ShowStatus((track.Automatic ? "Auto-captions" : "Creator captions") +
                    " available in " + CaptionLanguageName(track.Language), Theme.AccentSuccess);
                return true;
            }
            bool hasOtherTracks = TranscriptCaptions.ListTracks(captionSource).Count > 0;
            linkBar.ShowStatus(hasOtherTracks ? "Choose an available language from the caption menu." :
                "This source has no captions to download.", Theme.AccentWarning);
            return false;
        }

        private async void SubmitLink(string text)
        {
            if (submittingLink) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                StageClipboardLinks(false);
                text = linkBar.LinkText;
                if (string.IsNullOrWhiteSpace(text)) return;
            }
            List<string> urls = DownloadUrlValidator.ExtractUrls(text);
            if (urls.Count == 0)
            {
                linkBar.ShowStatus("Enter a valid link starting with http:// or https://.", Theme.AccentWarning);
                return;
            }
            submittingLink = true;
            DownloadKind kind = settings.SelectedKind;
            try
            {
                TranscriptCaptions.Source inspected = null;
                if (IsTranscriptMode && DownloadToolsReady && engineOperationRunning)
                {
                    linkBar.ShowStatus("Preparing download engines…", Theme.TextSecondary);
                    return;
                }
                if (IsTranscriptMode && DownloadToolsReady && !engineOperationRunning &&
                    urls.Count == 1 && !DownloadUrlValidator.IsPlaylistUrl(urls[0]))
                {
                    if (captionTask == null || captionTask.IsCompleted && captionSource == null) RefreshCaptionPreview();
                    Task<TranscriptCaptions.Source> current = captionTask;
                    if (current != null) inspected = await current;
                    if (IsDisposed || !Enabled || kind != settings.SelectedKind || text != linkBar.LinkText ||
                        current != captionTask || inspected == null || !ShowCaptionAvailability()) return;
                }
                List<string> remaining = ProcessInputText(text, InputSource.Paste, inspected);
                if (remaining == null || text != linkBar.LinkText) return;
                if (remaining.Count == 0) linkBar.Clear();
                else linkBar.Stage(string.Join(" ", remaining), false);
            }
            finally { submittingLink = false; }
        }
    }
}
