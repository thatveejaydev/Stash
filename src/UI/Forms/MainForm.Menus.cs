using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private static readonly CookieBrowser[] CookieBrowsers = (CookieBrowser[])Enum.GetValues(typeof(CookieBrowser));
        private sealed class SettingsRow
        {
            public PopupMenuRow Row;
            public Func<PopupMenuRow, Panel> BuildSubmenu;
        }

        private void UpdateBrowserCookiesTooltip()
        {
            appToolTip.SetToolTip(btnCookies, "Browser cookies: " + GetCookieShortName(settings.BrowserCookies));
        }

        private static string GetCookieShortName(CookieBrowser browser)
        {
            return browser == CookieBrowser.None ? "Off" : browser.ToString();
        }

        private string GetFolderShortDisplayName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return "Downloads";
            string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(leaf)) return fullPath;
            if (leaf.Length > 22) return leaf.Substring(0, 20) + "…";
            return leaf;
        }

        private string GetEmbedShortDisplayName()
        {
            if (settings.EmbedMetadata && settings.EmbedThumbnail) return "Tags and art";
            if (settings.EmbedMetadata) return "Tags";
            if (settings.EmbedThumbnail) return "Art";
            return "Off";
        }

        private string GetSubtitlesShortDisplayName()
        {
            if (settings.EmbedSubtitles && settings.IncludeAutoCaptions) return "All";
            if (settings.EmbedSubtitles) return "Creator only";
            if (settings.IncludeAutoCaptions) return "Auto only";
            return "Off";
        }

        private static string GetFileNameStyleShortName(FileNameStyle style)
        {
            switch (style)
            {
                case FileNameStyle.TitleWithId: return "Title [ID]";
                case FileNameStyle.UploaderAndTitle: return "Channel - Title";
                default: return "Title";
            }
        }

        private static void SetBadge(PopupMenuRow row, string badge)
        {
            if (row == null) return;
            SetBadgeCore(row, badge);
        }

        // The footer cookie button: only the browser choices, opened above the button.
        private void ShowCookiesPopup()
        {
            Panel content = BuildCookiesSubmenuPanel(null);
            ToolStripDropDown popup = BufferedDropDown.Create(content, true);
            popup.Closed += (s, e) => BeginInvoke((Action)popup.Dispose);
            popup.Show(btnCookies, new Point(btnCookies.Width - popup.Width, -popup.Height - Dpi.Px(6)));
        }

        private static void SetBadgeCore(PopupMenuRow row, string badge)
        {
            row.RightBadgeText = badge + "   ◂";
            row.Invalidate();
        }

        private static void SelectSubmenuRow(Panel panel, PopupMenuRow selectedRow)
        {
            foreach (Control control in panel.Controls)
            {
                PopupMenuRow row = control as PopupMenuRow;
                if (row == null || row.AllowRemove) continue;
                row.IsSelected = row == selectedRow;
                row.Invalidate();
            }
        }

        // ---- Settings popup ----

        // Advanced settings, with categories on the right and their choices on the left.
        private void ShowSettingsPopup(Control anchor)
        {
            int rowStep = Dpi.Px(34);
            int gap = Dpi.Px(4);
            bool transcript = IsTranscriptMode;
            int leftWidth = Dpi.Px(240);
            int rightMaxWidth = Dpi.Px(270);
            ToolStripDropDown popup = null;

            int separatorGap = Dpi.Px(9);
            int nextRowTop = Dpi.Px(4);
            List<int> separators = new List<int>();
            List<SettingsRow> rows = new List<SettingsRow>();
            Action<string, string, Func<PopupMenuRow, Panel>> addRow = (title, badge, build) =>
            {
                PopupMenuRow row = new PopupMenuRow(title, badge + "   ◂", null, false, "")
                {
                    Location = new Point(Dpi.Px(4), nextRowTop),
                    Width = leftWidth - Dpi.Px(8)
                };
                rows.Add(new SettingsRow { Row = row, BuildSubmenu = build });
                nextRowTop += rowStep;
            };
            // A thin line between groups of related settings.
            Action addSeparator = () =>
            {
                separators.Add(nextRowTop + separatorGap / 2);
                nextRowTop += separatorGap;
            };
            if (!transcript)
            {
                addRow("Subtitles", GetSubtitlesShortDisplayName(), BuildSubtitlesSubmenuPanel);
                addRow("Embed", GetEmbedShortDisplayName(), BuildEmbedSubmenuPanel);
            }
            addRow("File name", GetFileNameStyleShortName(settings.FileNameStyle), BuildFileNameSubmenuPanel);
            addSeparator();
            addRow("Parallel", settings.ParallelCount + " at once", BuildParallelSubmenuPanel);
            if (!transcript) addRow("Speed boost", settings.ConcurrentFragments + "x", BuildTurboSubmenuPanel);
            addSeparator();
            addRow("Cookies", GetCookieShortName(settings.BrowserCookies), BuildCookiesSubmenuPanel);
            addRow("Clipboard", settings.WatchClipboard ? "Watching" : "Off", BuildClipboardSubmenuPanel);

            int leftHeight = nextRowTop + Dpi.Px(4);
            // Build every submenu once per opening and reuse it; rebuilding on each hover caused
            // an empty frame while the new panel was created and painted.
            Panel[] submenus = new Panel[rows.Count];
            int totalHeight = leftHeight;
            for (int i = 0; i < rows.Count; i++)
            {
                submenus[i] = rows[i].BuildSubmenu(rows[i].Row);
                totalHeight = Math.Max(totalHeight, submenus[i].Height);
            }
            // The category list sits at the top; taller submenus extend below it.
            const int leftTop = 0;

            BufferedPanel rootContainer = new BufferedPanel { BackColor = Theme.BgSurface, Width = leftWidth + gap + rightMaxWidth, Height = totalHeight };
            Panel pnlLeft = Theme.CreatePopupPanel(leftWidth, leftHeight);
            pnlLeft.Location = new Point(rightMaxWidth + gap, leftTop);
            pnlLeft.Paint += (s, e) =>
            {
                using (Pen line = new Pen(Theme.BorderSubtle))
                    foreach (int y in separators) e.Graphics.DrawLine(line, Dpi.Px(12), y, pnlLeft.Width - Dpi.Px(12), y);
            };
            rootContainer.Controls.Add(pnlLeft);
            for (int i = 0; i < rows.Count; i++)
            {
                Panel submenu = submenus[i];
                int preferredTop = leftTop + rows[i].Row.Top - Dpi.Px(4);
                submenu.Location = new Point(pnlLeft.Left - gap - submenu.Width, Math.Max(0, Math.Min(preferredTop, totalHeight - submenu.Height)));
                submenu.Visible = false;
                rootContainer.Controls.Add(submenu);
            }

            popup = BufferedDropDown.Create(rootContainer, false);
            int currentSubmenuIndex = -1;

            // Only the category list and the open submenu are visible; the rest of the popup is clipped away.
            Action updatePopupRegion = () =>
            {
                Region reg = new Region(pnlLeft.Bounds);
                if (currentSubmenuIndex >= 0)
                {
                    Rectangle sub = submenus[currentSubmenuIndex].Bounds;
                    // Bridge the gap so moving left into a submenu stays inside the popup.
                    int top = Math.Max(pnlLeft.Top, sub.Top);
                    int bottom = Math.Min(pnlLeft.Bottom, sub.Bottom);
                    if (bottom > top) reg.Union(new Rectangle(sub.Right, top, gap, bottom - top));
                    reg.Union(sub);
                }
                Region previous = popup.Region;
                popup.Region = reg;
                if (previous != null) previous.Dispose();
            };

            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                SettingsRow option = rows[i];
                Action open = () =>
                {
                    if (currentSubmenuIndex == index) return;
                    rootContainer.SuspendLayout();
                    foreach (SettingsRow other in rows)
                    {
                        bool selected = other == option;
                        if (other.Row.IsSelected == selected) continue;
                        other.Row.IsSelected = selected;
                        other.Row.Invalidate();
                    }
                    int previous = currentSubmenuIndex;
                    currentSubmenuIndex = index;
                    submenus[index].Visible = true;
                    if (previous >= 0) submenus[previous].Visible = false;
                    rootContainer.ResumeLayout();
                    updatePopupRegion();
                };
                option.Row.MouseEnter += (s, e) => open();
                option.Row.MouseDown += (s, e) => open();
                pnlLeft.Controls.Add(option.Row);
            }

            updatePopupRegion();

            popup.Closed += (s, e) =>
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)popup.Dispose);
            };
            int x = anchor.Width - popup.Width;
            popup.Show(anchor, new Point(x, anchor.Height + Dpi.Px(6)));
        }

        // ---- Submenus ----

        private Panel BuildChoicePanel(int width, string[] titles, string[] tags, string[] subTexts, int selected,
            Action<int, PopupMenuRow, Panel> choose)
        {
            int rowH = subTexts == null ? Dpi.Px(34) : Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(width, titles.Length * rowH + Dpi.Px(8));
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                PopupMenuRow row = new PopupMenuRow(titles[i], tags == null ? null : tags[i], subTexts == null ? null : subTexts[i], i == selected)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + i * rowH),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    choose(index, row, container);
                    SelectSubmenuRow(container, row);
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private Panel BuildSaveToSubmenuPanel(Func<ToolStripDropDown> popup, Control anchor)
        {
            int width = Dpi.Px(270);
            int rowH = Dpi.Px(46);
            List<string> folders = new List<string>(settings.RecentFolders);
            Panel container = Theme.CreatePopupPanel(width, folders.Count * rowH + Dpi.Px(34) + Dpi.Px(14));
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderSubtle))
                {
                    int y = folders.Count * rowH + Dpi.Px(7);
                    e.Graphics.DrawLine(p, Dpi.Px(8), y, container.Width - Dpi.Px(8), y);
                }
            };
            for (int i = 0; i < folders.Count; i++)
            {
                string folderPath = folders[i];
                string leaf = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(leaf)) leaf = folderPath;
                PopupMenuRow row = new PopupMenuRow(leaf, null, folderPath,
                    string.Equals(folderPath, settings.CurrentOutputFolder, StringComparison.OrdinalIgnoreCase))
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + i * rowH),
                    Width = width - Dpi.Px(8),
                    AllowRemove = folders.Count > 1
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    SelectOutputFolder(folderPath);
                    foreach (Control c in container.Controls)
                    {
                        PopupMenuRow other = c as PopupMenuRow;
                        if (other == null || !other.AllowRemove && other.SubText == null) continue;
                        other.IsSelected = other == row;
                        other.Invalidate();
                    }
                };
                row.RemoveClicked += (s, e) =>
                {
                    settings.RemoveFolderFromHistory(folderPath);
                    UpdateHeaderSettings();
                    ToolStripDropDown current = popup();
                    if (current != null) current.Close();
                    BeginInvoke((Action)(() => ShowFolderPopup(anchor)));
                };
                container.Controls.Add(row);
            }

            PopupMenuRow browse = new PopupMenuRow("Choose another folder…", null, null, false, "")
            {
                Location = new Point(Dpi.Px(4), folders.Count * rowH + Dpi.Px(12)),
                Width = width - Dpi.Px(8),
                TitleOverrideColor = Theme.AccentText
            };
            browse.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ToolStripDropDown current = popup();
                if (current != null) current.Close();
                BeginInvoke((Action)PromptBrowseForFolder);
            };
            container.Controls.Add(browse);
            return container;
        }

        private void SelectOutputFolder(string folderPath)
        {
            settings.AddFolderToHistory(folderPath);
            UpdateHeaderSettings();
        }

        private void PromptBrowseForFolder()
        {
            string chosen = ModernFolderPicker.ShowDialog(Handle, settings.CurrentOutputFolder, "Choose where downloads are saved");
            if (!string.IsNullOrWhiteSpace(chosen)) SelectOutputFolder(chosen);
        }

        private Panel BuildToggleRow(Panel container, int top, string title, string subText, Func<bool> get, Action<bool> set, Action changed)
        {
            PopupMenuRow row = new PopupMenuRow(title, null, subText, get(), get() ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), top),
                Width = container.Width - Dpi.Px(8)
            };
            row.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                set(!get());
                settings.Save();
                row.IsSelected = get();
                row.CheckGlyph = get() ? "☑" : "☐";
                row.Invalidate();
                changed();
            };
            container.Controls.Add(row);
            return container;
        }

        private Panel BuildSubtitlesSubmenuPanel(PopupMenuRow parentRow)
        {
            int rowH = Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(Dpi.Px(260), 2 * rowH + Dpi.Px(8));
            Action changed = () => SetBadge(parentRow, GetSubtitlesShortDisplayName());
            BuildToggleRow(container, Dpi.Px(4), "Creator subtitles", "All languages, toggle in your player",
                () => settings.EmbedSubtitles, value => settings.EmbedSubtitles = value, changed);
            BuildToggleRow(container, Dpi.Px(4) + rowH, "Auto-generated captions", "English and the original language",
                () => settings.IncludeAutoCaptions, value => settings.IncludeAutoCaptions = value, changed);
            return container;
        }

        private Panel BuildEmbedSubmenuPanel(PopupMenuRow parentRow)
        {
            int rowH = Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(Dpi.Px(250), 2 * rowH + Dpi.Px(8));
            Action changed = () => SetBadge(parentRow, GetEmbedShortDisplayName());
            BuildToggleRow(container, Dpi.Px(4), "Tags and chapters", "Title, artist, date and chapters",
                () => settings.EmbedMetadata, value => settings.EmbedMetadata = value, changed);
            BuildToggleRow(container, Dpi.Px(4) + rowH, "Cover art", "Thumbnail as the file's artwork",
                () => settings.EmbedThumbnail, value => settings.EmbedThumbnail = value, changed);
            return container;
        }

        private Panel BuildParallelSubmenuPanel(PopupMenuRow parentRow)
        {
            string[] titles = new string[6];
            for (int n = 1; n <= 6; n++) titles[n - 1] = n + (n == 1 ? " download" : " downloads");
            return BuildChoicePanel(Dpi.Px(200), titles, null, null, settings.ParallelCount - 1, (index, row, panel) =>
            {
                settings.ParallelCount = index + 1;
                settings.Save();
                SetBadge(parentRow, settings.ParallelCount + " at once");
                ProcessQueue();
            });
        }

        private Panel BuildTurboSubmenuPanel(PopupMenuRow parentRow)
        {
            int[] counts = { 1, 4, 8, 12, 16 };
            string[] titles = { "1x", "4x", "8x", "12x", "16x" };
            string[] tags = { "Single connection", "Fast", "Recommended", "Faster", "Maximum" };
            return BuildChoicePanel(Dpi.Px(240), titles, tags, null, Array.IndexOf(counts, settings.ConcurrentFragments), (index, row, panel) =>
            {
                settings.ConcurrentFragments = counts[index];
                settings.Save();
                SetBadge(parentRow, counts[index] + "x");
            });
        }

        private Panel BuildFileNameSubmenuPanel(PopupMenuRow parentRow)
        {
            FileNameStyle[] styles = { FileNameStyle.Title, FileNameStyle.TitleWithId, FileNameStyle.UploaderAndTitle };
            string[] titles = new string[styles.Length];
            for (int i = 0; i < styles.Length; i++) titles[i] = GetFileNameStyleShortName(styles[i]);
            string[] examples = { "My video.mp4", "My video [dQw4w9WgXcQ].mp4", "Channel name - My video.mp4" };
            return BuildChoicePanel(Dpi.Px(260), titles, null, examples, Array.IndexOf(styles, settings.FileNameStyle), (index, row, panel) =>
            {
                settings.FileNameStyle = styles[index];
                settings.Save();
                SetBadge(parentRow, GetFileNameStyleShortName(styles[index]));
            });
        }

        private Panel BuildCookiesSubmenuPanel(PopupMenuRow parentRow)
        {
            string[] titles = new string[CookieBrowsers.Length];
            string[] tags = new string[CookieBrowsers.Length];
            for (int i = 0; i < CookieBrowsers.Length; i++)
            {
                titles[i] = CookieBrowsers[i] == CookieBrowser.None ? "Don't use cookies" : CookieBrowsers[i].ToString();
                tags[i] = CookieBrowsers[i] == CookieBrowser.Firefox ? "Recommended" : null;
            }
            Panel panel = BuildChoicePanel(Dpi.Px(250), titles, tags, null, Array.IndexOf(CookieBrowsers, settings.BrowserCookies), (index, row, container) =>
            {
                settings.BrowserCookies = CookieBrowsers[index];
                settings.Save();
                SetBadge(parentRow, GetCookieShortName(settings.BrowserCookies));
                UpdateBrowserCookiesTooltip();
                RefreshCaptionPreview();
            });
            // Explain when cookies help, below the choices.
            Label note = new Label
            {
                Text = "Use a signed-in browser for private or age-restricted links. Chrome-based browsers often block access after updates.",
                Font = Theme.GetFont("Segoe UI", 8.25F),
                ForeColor = Theme.TextMuted,
                BackColor = Theme.BgSurface,
                Location = new Point(Dpi.Px(12), panel.Height),
                MaximumSize = new Size(panel.Width - Dpi.Px(24), 0),
                AutoSize = true,
                UseMnemonic = false
            };
            panel.Controls.Add(note);
            panel.Height = note.Bottom + Dpi.Px(10);
            return panel;
        }

        private Panel BuildClipboardSubmenuPanel(PopupMenuRow parentRow)
        {
            Panel container = Theme.CreatePopupPanel(Dpi.Px(260), Dpi.Px(46) + Dpi.Px(8));
            BuildToggleRow(container, Dpi.Px(4), "Watch clipboard for links", "Queue links you copy in other apps",
                () => settings.WatchClipboard, value => settings.WatchClipboard = value, () =>
                {
                    UpdateClipboardWatch();
                    SetBadge(parentRow, settings.WatchClipboard ? "Watching" : "Off");
                });
            return container;
        }
    }
}
