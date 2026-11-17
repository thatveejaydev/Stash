using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm
    {
        private void UpdateQualitySelectorEnabledState()
        {
            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            bool isVideo = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            selQuality.Enabled = toggleSmartMode.Checked && isVideo;
        }

        // Dark Popup Helper for InlineFlowSelectors
        private ToolStripDropDown CreateDarkPopup(Control content, InlineFlowSelector ownerSelector, bool alignRight = false)
        {
            ToolStripControlHost host = new ToolStripControlHost(content)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = content.Size
            };

            ToolStripDropDown popup = new ToolStripDropDown
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = content.Size,
                BackColor = Theme.BgSurface,
                DropShadowEnabled = true
            };
            popup.Items.Add(host);
            popup.Closed += (s, e) =>
            {
                ownerSelector.SetOpenState(false);
                // Popups are rebuilt on every open; release the old one's window handles.
                BeginInvoke((Action)popup.Dispose);
            };
            ownerSelector.SetOpenState(true);
            int x = alignRight ? ownerSelector.Width - popup.Width : 0;
            popup.Show(ownerSelector, new Point(x, ownerSelector.Height + Dpi.Px(2)));
            return popup;
        }

        // Perfectly aligned Download Format dropdown with fixed left checkmark column and right-aligned detail tag
        private void ShowFormatPopup()
        {
            int width = Dpi.Px(265);
            Panel container = Theme.CreatePopupPanel(width, FormatPopupTitles.Length * Dpi.Px(34) + Dpi.Px(8));

            ToolStripDropDown popup = null;
            for (int i = 0; i < FormatPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.FormatIndex);
                PopupMenuRow row = new PopupMenuRow(FormatPopupTitles[idx], FormatPopupRightTags[idx], null, active)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + idx * Dpi.Px(34)),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    settings.FormatIndex = idx;
                    settings.Save();
                    selDownloadFormat.SetValueLabel(FormatShortNames[idx]);
                    UpdateQualitySelectorEnabledState();
                    if (popup != null) popup.Close();
                };
                container.Controls.Add(row);
            }
            popup = CreateDarkPopup(container, selDownloadFormat);
        }

        private void ShowQualityPopup()
        {
            int width = Dpi.Px(245);
            Panel container = Theme.CreatePopupPanel(width, QualityPopupTitles.Length * Dpi.Px(34) + Dpi.Px(8));

            ToolStripDropDown popup = null;
            for (int i = 0; i < QualityPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.QualityIndex);
                PopupMenuRow row = new PopupMenuRow(QualityPopupTitles[idx], QualityPopupRightTags[idx], null, active)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + idx * Dpi.Px(34)),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    settings.QualityIndex = idx;
                    settings.Save();
                    selQuality.SetValueLabel(QualityShortNames[idx]);
                    if (popup != null) popup.Close();
                };
                container.Controls.Add(row);
            }
            popup = CreateDarkPopup(container, selQuality);
        }

        // Save-to Dropdown: Folder Name on main line, full path in tiny text directly underneath, plus '✕' button to remove from list
        private void ShowSaveToHistoryPopup()
        {
            int width = Dpi.Px(325);
            int itemCount = settings.RecentFolders.Count;
            int rowH = Dpi.Px(46);
            int totalHeight = (itemCount * rowH) + Dpi.Px(46);

            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = totalHeight
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                    int sepY = itemCount * rowH + Dpi.Px(6);
                    e.Graphics.DrawLine(p, Dpi.Px(8), sepY, container.Width - Dpi.Px(8), sepY);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < itemCount; i++)
            {
                string folderPath = settings.RecentFolders[i];
                bool isCurrent = string.Equals(folderPath, settings.CurrentOutputFolder, StringComparison.OrdinalIgnoreCase);
                string leaf = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrEmpty(leaf)) leaf = folderPath;

                PopupMenuRow row = new PopupMenuRow(leaf, null, folderPath, isCurrent)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + i * rowH),
                    Width = width - Dpi.Px(8),
                    AllowRemove = true
                };
                row.MouseDown += (s, e) =>
                {
                    SelectOutputFolder(folderPath);
                    if (popup != null) popup.Close();
                };
                row.RemoveClicked += (s, e) =>
                {
                    settings.RemoveFolderFromHistory(folderPath);
                    selSaveTo.SetValueLabel(GetFolderShortDisplayName(settings.CurrentOutputFolder));
                    appToolTip.SetToolTip(selSaveTo, settings.CurrentOutputFolder);
                    if (popup != null) popup.Close();
                    BeginInvoke((Action)ShowSaveToHistoryPopup);
                };
                container.Controls.Add(row);
            }

            // "Browse for folder..." row at bottom of popup
            PopupMenuRow browseRow = new PopupMenuRow("Browse for folder...", null, null, false, "")
            {
                Location = new Point(Dpi.Px(4), itemCount * rowH + Dpi.Px(9)),
                Width = width - Dpi.Px(8),
                TitleOverrideColor = Theme.BrandPurpleLight
            };
            browseRow.MouseDown += (s, e) =>
            {
                if (popup != null) popup.Close();
                BeginInvoke((Action)PromptBrowseForFolder);
            };
            container.Controls.Add(browseRow);

            popup = CreateDarkPopup(container, selSaveTo);
        }

        private void SelectOutputFolder(string folderPath)
        {
            settings.AddFolderToHistory(folderPath);
            selSaveTo.SetValueLabel(GetFolderShortDisplayName(folderPath));
            appToolTip.SetToolTip(selSaveTo, folderPath);
        }

        // Opens the full modern Windows File Explorer dialog instead of the tiny tree window
        private void PromptBrowseForFolder()
        {
            string chosen = ModernFolderPicker.ShowDialog(
                Handle,
                settings.CurrentOutputFolder,
                "Select Destination Folder for Downloads");

            if (!string.IsNullOrWhiteSpace(chosen))
            {
                SelectOutputFolder(chosen);
            }
        }

        private static readonly CookieBrowser[] CookieBrowsers = (CookieBrowser[])Enum.GetValues(typeof(CookieBrowser));

        private sealed class OptionsRow
        {
            public PopupMenuRow Row;
            public Func<PopupMenuRow, Panel> BuildSubmenu;
        }

        private void ShowOptionsNestedPopup()
        {
            int rowStep = Dpi.Px(34);
            int gap = Dpi.Px(4);
            int leftWidth = Dpi.Px(225);
            int rightMaxWidth = Dpi.Px(250);

            List<OptionsRow> rows = new List<OptionsRow>();
            Action<string, string, Func<PopupMenuRow, Panel>> addRow = (title, badge, build) =>
            {
                PopupMenuRow row = new PopupMenuRow(title, badge + "   ◂", null, false, "")
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + rows.Count * rowStep),
                    Width = leftWidth - Dpi.Px(8)
                };
                rows.Add(new OptionsRow { Row = row, BuildSubmenu = build });
            };
            addRow("Subtitles", GetSubtitlesShortDisplayName(), BuildSubtitlesSubmenuPanel);
            addRow("Embed", GetEmbedShortDisplayName(), BuildEmbedSubmenuPanel);
            addRow("Parallel", settings.ParallelCount + (settings.ParallelCount == 1 ? " Download" : " Downloads"), BuildParallelSubmenuPanel);
            addRow("Turbo", settings.ConcurrentFragments + "x", BuildTurboSubmenuPanel);
            addRow("File name", GetFileNameStyleShortName(settings.FileNameStyle), BuildFileNameSubmenuPanel);
            addRow("Clipboard", settings.WatchClipboard ? "Watching" : "Off", BuildClipboardSubmenuPanel);

            int leftHeight = rows.Count * rowStep + Dpi.Px(8);
            // Tallest submenu decides the popup height: each opens aligned with its row.
            int totalHeight = leftHeight;
            for (int i = 0; i < rows.Count; i++)
            {
                using (Panel probe = rows[i].BuildSubmenu(rows[i].Row))
                    totalHeight = Math.Max(totalHeight, i * rowStep + probe.Height);
            }

            Panel rootContainer = new Panel
            {
                BackColor = Theme.BgWindow,
                Width = leftWidth + gap + rightMaxWidth,
                Height = totalHeight
            };

            Panel pnlLeft = Theme.CreatePopupPanel(leftWidth, leftHeight);
            pnlLeft.Location = new Point(rightMaxWidth + gap, 0);

            Panel pnlRightHost = new Panel
            {
                BackColor = Theme.BgSurface,
                Location = new Point(0, 0),
                Width = rightMaxWidth,
                Height = Dpi.Px(76),
                Visible = false
            };

            ToolStripControlHost host = new ToolStripControlHost(rootContainer)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = rootContainer.Size
            };

            ToolStripDropDown popup = new ToolStripDropDown
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = rootContainer.Size,
                BackColor = Theme.BgSurface,
                DropShadowEnabled = false
            };
            popup.Items.Add(host);

            Action updatePopupRegion = () =>
            {
                Region reg = new Region(pnlLeft.Bounds);
                if (pnlRightHost.Visible)
                {
                    // Bridge the gap so moving left into a submenu stays inside the popup.
                    Rectangle bridge = new Rectangle(pnlRightHost.Right, pnlRightHost.Top, gap, Math.Min(pnlLeft.Bottom, pnlRightHost.Bottom) - pnlRightHost.Top);
                    if (bridge.Height > 0) reg.Union(bridge);
                    reg.Union(pnlRightHost.Bounds);
                }
                Region previous = popup.Region;
                popup.Region = reg;
                if (previous != null) previous.Dispose();
            };

            int currentSubmenuIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                OptionsRow option = rows[i];
                Action open = () =>
                {
                    if (currentSubmenuIndex == index && pnlRightHost.Visible) return;
                    currentSubmenuIndex = index;
                    foreach (OptionsRow other in rows)
                    {
                        other.Row.IsSelected = other == option;
                        other.Row.Invalidate();
                    }
                    Panel childPanel = option.BuildSubmenu(option.Row);
                    pnlRightHost.SuspendLayout();
                    // Dispose the previous submenu instead of leaking its handles.
                    List<Control> old = new List<Control>();
                    foreach (Control c in pnlRightHost.Controls) old.Add(c);
                    pnlRightHost.Controls.Clear();
                    foreach (Control c in old) c.Dispose();
                    pnlRightHost.Location = new Point(pnlLeft.Left - gap - childPanel.Width, index * rowStep);
                    pnlRightHost.Size = childPanel.Size;
                    childPanel.Location = Point.Empty;
                    pnlRightHost.Controls.Add(childPanel);
                    pnlRightHost.Visible = true;
                    pnlRightHost.ResumeLayout();
                    updatePopupRegion();
                };
                option.Row.MouseEnter += (s, e) => open();
                option.Row.MouseDown += (s, e) => open();
                pnlLeft.Controls.Add(option.Row);
            }

            rootContainer.Controls.Add(pnlLeft);
            rootContainer.Controls.Add(pnlRightHost);

            updatePopupRegion();

            popup.Closed += (s, e) =>
            {
                selOptions.SetOpenState(false);
                BeginInvoke((Action)popup.Dispose);
            };
            selOptions.SetOpenState(true);
            popup.Show(selOptions, new Point(selOptions.Width - popup.Width, selOptions.Height + Dpi.Px(2)));
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

        private Panel BuildFileNameSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(250);
            int rowH = Dpi.Px(46);
            FileNameStyle[] styles = { FileNameStyle.Title, FileNameStyle.TitleWithId, FileNameStyle.UploaderAndTitle };
            string[] examples = { "My Video.mp4", "My Video [dQw4w9WgXcQ].mp4", "Channel Name - My Video.mp4" };
            Panel container = Theme.CreatePopupPanel(width, styles.Length * rowH + Dpi.Px(8));
            for (int i = 0; i < styles.Length; i++)
            {
                FileNameStyle style = styles[i];
                PopupMenuRow row = new PopupMenuRow(GetFileNameStyleShortName(style), null, examples[i], style == settings.FileNameStyle)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + i * rowH),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    settings.FileNameStyle = style;
                    settings.Save();
                    parentRow.RightBadgeText = GetFileNameStyleShortName(style) + "   ◂";
                    parentRow.Invalidate();
                    SelectSubmenuRow(container, row);
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private Panel BuildClipboardSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(250);
            Panel container = Theme.CreatePopupPanel(width, Dpi.Px(46) + Dpi.Px(8));
            PopupMenuRow row = new PopupMenuRow("Watch clipboard for links", null, "Queue links copied in other apps",
                settings.WatchClipboard, settings.WatchClipboard ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(4)),
                Width = width - Dpi.Px(8)
            };
            row.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                settings.WatchClipboard = !settings.WatchClipboard;
                settings.Save();
                UpdateClipboardWatch();
                row.IsSelected = settings.WatchClipboard;
                row.CheckGlyph = settings.WatchClipboard ? "☑" : "☐";
                row.Invalidate();
                parentRow.RightBadgeText = (settings.WatchClipboard ? "Watching" : "Off") + "   ◂";
                parentRow.Invalidate();
            };
            container.Controls.Add(row);
            return container;
        }

        private void UpdateBrowserCookiesTooltip()
        {
            appToolTip.SetToolTip(selBrowserCookies, "Browser Cookies (" + settings.BrowserCookies + ")");
        }

        private void ShowBrowserCookiesPopup()
        {
            int width = Dpi.Px(360);
            int rowHeight = Dpi.Px(34);
            int inset = Dpi.Px(12);
            Panel panel = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width
            };
            panel.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Theme.BorderHover))
                    e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - Dpi.Px(1), panel.Height - Dpi.Px(1));
            };

            const string recommendation = "Firefox is recommended.";
            RichTextBox description = new RichTextBox
            {
                Font = Font,
                BackColor = Theme.BgSurface,
                ForeColor = Theme.TextSecondary,
                Location = new Point(inset, inset),
                Width = width - 2 * inset,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                TabStop = false,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,
                Cursor = Cursors.Default
            };
            panel.Controls.Add(description);
            description.ContentsResized += (s, e) => description.Height = e.NewRectangle.Height + Dpi.Px(4);
            description.Text = "Use cookies from a browser to gain access to a url that requires authentication. Chrome-based " +
                "browsers (Chrome, Edge, Brave, Opera, Vivaldi, Chromium) are not recommended due to " +
                "frequent updates that may break cookie access. " + recommendation;
            description.Select(description.TextLength - recommendation.Length, recommendation.Length);
            using (Font bold = new Font(Font, FontStyle.Bold))
                description.SelectionFont = bold;
            description.Select(0, 0);

            PopupMenuRow selector = new PopupMenuRow("Choose browser", settings.BrowserCookies.ToString(), null, false, "")
            {
                Location = new Point(inset, description.Bottom + Dpi.Px(10)),
                Width = width - 2 * inset,
                DropdownExpanded = false
            };
            int collapsedHeight = selector.Bottom + inset;
            panel.Height = collapsedHeight;
            Panel browsers = new Panel
            {
                Location = new Point(inset, collapsedHeight),
                Size = new Size(selector.Width, CookieBrowsers.Length * rowHeight),
                Visible = false
            };
            panel.Controls.Add(selector);
            panel.Controls.Add(browsers);

            for (int i = 0; i < CookieBrowsers.Length; i++)
            {
                CookieBrowser browser = CookieBrowsers[i];
                string hint = browser == CookieBrowser.Firefox ? "Recommended" : null;
                PopupMenuRow row = new PopupMenuRow(browser.ToString(), hint, null, browser == settings.BrowserCookies)
                {
                    Location = new Point(0, i * rowHeight),
                    Width = browsers.Width
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    settings.BrowserCookies = browser;
                    settings.Save();
                    UpdateBrowserCookiesTooltip();
                    selector.RightBadgeText = browser.ToString();
                    selector.Invalidate();
                    SelectSubmenuRow(browsers, row);
                };
                browsers.Controls.Add(row);
            }
            ToolStripDropDown popup = CreateDarkPopup(panel, selBrowserCookies, alignRight: true);
            selector.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool expand = !browsers.Visible;
                browsers.Visible = expand;
                selector.DropdownExpanded = expand;
                selector.Invalidate();
                panel.Height = expand ? browsers.Bottom + inset : collapsedHeight;
                popup.Items[0].Size = panel.Size;
                popup.Size = panel.Size;
            };
        }

        private static void SelectSubmenuRow(Panel panel, PopupMenuRow selectedRow)
        {
            foreach (PopupMenuRow row in panel.Controls)
            {
                row.IsSelected = row == selectedRow;
                row.Invalidate();
            }
        }

        private Panel BuildSubtitlesSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(250);
            int rowH = Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(width, 2 * rowH + Dpi.Px(8));

            PopupMenuRow rowSubs = new PopupMenuRow(
                "Creator Subtitles",
                null,
                "All languages, toggle on/off in player",
                settings.EmbedSubtitles,
                settings.EmbedSubtitles ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(4)),
                Width = width - Dpi.Px(8)
            };
            rowSubs.MouseDown += (s, e) =>
            {
                settings.EmbedSubtitles = !settings.EmbedSubtitles;
                settings.Save();
                rowSubs.IsSelected = settings.EmbedSubtitles;
                rowSubs.CheckGlyph = settings.EmbedSubtitles ? "☑" : "☐";
                rowSubs.Invalidate();
                parentRow.RightBadgeText = GetSubtitlesShortDisplayName() + "   ◂";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            PopupMenuRow rowAuto = new PopupMenuRow(
                "Auto-Generated Captions",
                null,
                "English + original language",
                settings.IncludeAutoCaptions,
                settings.IncludeAutoCaptions ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(4) + rowH),
                Width = width - Dpi.Px(8)
            };
            rowAuto.MouseDown += (s, e) =>
            {
                settings.IncludeAutoCaptions = !settings.IncludeAutoCaptions;
                settings.Save();
                rowAuto.IsSelected = settings.IncludeAutoCaptions;
                rowAuto.CheckGlyph = settings.IncludeAutoCaptions ? "☑" : "☐";
                rowAuto.Invalidate();
                parentRow.RightBadgeText = GetSubtitlesShortDisplayName() + "   ◂";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            container.Controls.Add(rowSubs);
            container.Controls.Add(rowAuto);
            return container;
        }

        private Panel BuildEmbedSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(235);
            Panel container = Theme.CreatePopupPanel(width, Dpi.Px(76));

            PopupMenuRow rowMeta = new PopupMenuRow(
                "Metadata & Chapters",
                null,
                null,
                settings.EmbedMetadata,
                settings.EmbedMetadata ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(4)),
                Width = width - Dpi.Px(8)
            };
            rowMeta.MouseDown += (s, e) =>
            {
                settings.EmbedMetadata = !settings.EmbedMetadata;
                settings.Save();
                rowMeta.IsSelected = settings.EmbedMetadata;
                rowMeta.CheckGlyph = settings.EmbedMetadata ? "☑" : "☐";
                rowMeta.Invalidate();
                parentRow.RightBadgeText = GetEmbedShortDisplayName() + "   ◂";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            PopupMenuRow rowThumb = new PopupMenuRow(
                "Thumbnail Cover Art",
                null,
                null,
                settings.EmbedThumbnail,
                settings.EmbedThumbnail ? "☑" : "☐")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(38)),
                Width = width - Dpi.Px(8)
            };
            rowThumb.MouseDown += (s, e) =>
            {
                settings.EmbedThumbnail = !settings.EmbedThumbnail;
                settings.Save();
                rowThumb.IsSelected = settings.EmbedThumbnail;
                rowThumb.CheckGlyph = settings.EmbedThumbnail ? "☑" : "☐";
                rowThumb.Invalidate();
                parentRow.RightBadgeText = GetEmbedShortDisplayName() + "   ◂";
                parentRow.Invalidate();
                selOptions.SetValueLabel(GetOptionsSummaryLabel());
            };

            container.Controls.Add(rowMeta);
            container.Controls.Add(rowThumb);
            return container;
        }

        private Panel BuildParallelSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(190);
            int rowH = Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(width, 6 * rowH + Dpi.Px(8));

            for (int n = 1; n <= 6; n++)
            {
                int count = n;
                bool active = (count == settings.ParallelCount);
                string title = count + (count == 1 ? " Download" : " Downloads");
                PopupMenuRow row = new PopupMenuRow(title, null, "Concurrent", active)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + (n - 1) * rowH),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    settings.ParallelCount = count;
                    settings.Save();
                    parentRow.RightBadgeText = count + (count == 1 ? " Download   ◂" : " Downloads   ◂");
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    SelectSubmenuRow(container, row);
                    ProcessQueue();
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private Panel BuildTurboSubmenuPanel(PopupMenuRow parentRow)
        {
            int[] counts = new int[] { 1, 4, 8, 12, 16 };
            string[] titles = new string[]
            {
                "1x  (Standard)",
                "4x  (Fast)",
                "8x  (Turbo Default)",
                "12x  (High Speed)",
                "16x  (Maximum)"
            };
            string[] subTags = new string[]
            {
                "Single Stream",
                "4 Connections",
                "8 Connections",
                "12 Connections",
                "16 Connections"
            };

            int width = Dpi.Px(210);
            int rowH = Dpi.Px(46);
            Panel container = Theme.CreatePopupPanel(width, counts.Length * rowH + Dpi.Px(8));

            for (int i = 0; i < counts.Length; i++)
            {
                int fragCount = counts[i];
                bool active = (fragCount == settings.ConcurrentFragments);
                PopupMenuRow row = new PopupMenuRow(titles[i], null, subTags[i], active)
                {
                    Location = new Point(Dpi.Px(4), Dpi.Px(4) + i * rowH),
                    Width = width - Dpi.Px(8)
                };
                row.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    settings.ConcurrentFragments = fragCount;
                    settings.Save();
                    parentRow.RightBadgeText = fragCount + "x   ◂";
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    SelectSubmenuRow(container, row);
                };
                container.Controls.Add(row);
            }
            return container;
        }
    }
}
