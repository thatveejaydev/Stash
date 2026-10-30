using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MediaDownloader
{
    public partial class MainForm : Form
    {
        private void UpdateQualitySelectorEnabledState()
        {
            DownloadMode mode = (DownloadMode)settings.FormatIndex;
            bool isVideo = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);
            selQuality.Enabled = toggleSmartMode.Checked && isVideo;
        }

        // Dark Popup Helper for InlineFlowSelectors
        private ToolStripDropDown CreateDarkPopup(Control content, InlineFlowSelector ownerSelector)
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
            popup.Closed += (s, e) => ownerSelector.SetOpenState(false);
            ownerSelector.SetOpenState(true);
            popup.Show(ownerSelector, new Point(0, ownerSelector.Height + Dpi.Px(2)));
            return popup;
        }

        // Perfectly aligned Download Format dropdown with fixed left checkmark column and right-aligned detail tag
        private void ShowFormatPopup()
        {
            int width = Dpi.Px(265);
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = FormatPopupTitles.Length * Dpi.Px(34) + Dpi.Px(8)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = QualityPopupTitles.Length * Dpi.Px(34) + Dpi.Px(8)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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

        // Nested Dropdown containing "Subtitles", "Embed", "Parallel", and "Turbo" submenus inside a single Region-shaped popup
        private void ShowOptionsNestedPopup()
        {
            int leftWidth = Dpi.Px(225);
            int leftHeight = 4 * Dpi.Px(34) + Dpi.Px(8); // 144
            int gap = Dpi.Px(4);
            int rightMaxWidth = Dpi.Px(250);
            int totalWidth = leftWidth + gap + rightMaxWidth;
            int totalHeight = Dpi.Px(102) + (6 * Dpi.Px(46) + Dpi.Px(8)) + Dpi.Px(16); // Accommodate 4 parent rows + child submenus aligned up to alignY=102

            Panel rootContainer = new Panel
            {
                BackColor = Theme.BgWindow,
                Width = totalWidth,
                Height = totalHeight
            };

            Panel pnlLeft = new Panel
            {
                BackColor = Theme.BgSurface,
                Location = new Point(rightMaxWidth + gap, 0),
                Width = leftWidth,
                Height = leftHeight
            };
            pnlLeft.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, pnlLeft.Width - Dpi.Px(1), pnlLeft.Height - Dpi.Px(1));
                }
            };

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
                popup.Region = reg;
            };

            PopupMenuRow rowSubtitles = new PopupMenuRow(
                "Subtitles",
                GetSubtitlesShortDisplayName() + "   ◂",
                null,
                false,
                "")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(4)),
                Width = leftWidth - Dpi.Px(8)
            };

            PopupMenuRow rowEmbed = new PopupMenuRow(
                "Embed",
                GetEmbedShortDisplayName() + "   ◂",
                null,
                false,
                "")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(38)),
                Width = leftWidth - Dpi.Px(8)
            };

            PopupMenuRow rowParallel = new PopupMenuRow(
                "Parallel",
                settings.ParallelCount + (settings.ParallelCount == 1 ? " Download   ◂" : " Downloads   ◂"),
                null,
                false,
                "")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(72)),
                Width = leftWidth - Dpi.Px(8)
            };

            PopupMenuRow rowTurbo = new PopupMenuRow(
                "Turbo",
                settings.ConcurrentFragments + "x   ◂",
                null,
                false,
                "")
            {
                Location = new Point(Dpi.Px(4), Dpi.Px(106)),
                Width = leftWidth - Dpi.Px(8)
            };

            int currentSubmenuIndex = -1;

            Action<int, PopupMenuRow, Panel, int> showSubmenu = (index, ownerRow, childPanel, alignY) =>
            {
                if (currentSubmenuIndex == index && pnlRightHost.Visible) return;
                currentSubmenuIndex = index;

                rowSubtitles.IsSelected = (ownerRow == rowSubtitles);
                rowEmbed.IsSelected = (ownerRow == rowEmbed);
                rowParallel.IsSelected = (ownerRow == rowParallel);
                rowTurbo.IsSelected = (ownerRow == rowTurbo);
                rowSubtitles.Invalidate();
                rowEmbed.Invalidate();
                rowParallel.Invalidate();
                rowTurbo.Invalidate();

                pnlRightHost.SuspendLayout();
                pnlRightHost.Controls.Clear();
                pnlRightHost.Location = new Point(pnlLeft.Left - gap - childPanel.Width, alignY);
                pnlRightHost.Size = childPanel.Size;
                childPanel.Location = Point.Empty;
                pnlRightHost.Controls.Add(childPanel);
                pnlRightHost.Visible = true;
                pnlRightHost.ResumeLayout();

                updatePopupRegion();
            };

            Action openSubtitles = () =>
            {
                Panel sub = BuildSubtitlesSubmenuPanel(rowSubtitles);
                showSubmenu(0, rowSubtitles, sub, 0);
            };

            Action openEmbed = () =>
            {
                Panel sub = BuildEmbedSubmenuPanel(rowEmbed);
                showSubmenu(1, rowEmbed, sub, Dpi.Px(34));
            };

            Action openParallel = () =>
            {
                Panel sub = BuildParallelSubmenuPanel(rowParallel, () => popup.Close());
                showSubmenu(2, rowParallel, sub, Dpi.Px(68));
            };

            Action openTurbo = () =>
            {
                Panel sub = BuildTurboSubmenuPanel(rowTurbo, () => popup.Close());
                showSubmenu(3, rowTurbo, sub, Dpi.Px(102));
            };

            rowSubtitles.MouseEnter += (s, e) => openSubtitles();
            rowSubtitles.MouseDown += (s, e) => openSubtitles();

            rowEmbed.MouseEnter += (s, e) => openEmbed();
            rowEmbed.MouseDown += (s, e) => openEmbed();

            rowParallel.MouseEnter += (s, e) => openParallel();
            rowParallel.MouseDown += (s, e) => openParallel();

            rowTurbo.MouseEnter += (s, e) => openTurbo();
            rowTurbo.MouseDown += (s, e) => openTurbo();

            pnlLeft.Controls.Add(rowSubtitles);
            pnlLeft.Controls.Add(rowEmbed);
            pnlLeft.Controls.Add(rowParallel);
            pnlLeft.Controls.Add(rowTurbo);

            rootContainer.Controls.Add(pnlLeft);
            rootContainer.Controls.Add(pnlRightHost);

            updatePopupRegion();

            popup.Closed += (s, e) => selOptions.SetOpenState(false);
            selOptions.SetOpenState(true);
            popup.Show(selOptions, new Point(selOptions.Width - popup.Width, selOptions.Height + Dpi.Px(2)));
        }

        private Panel BuildSubtitlesSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = Dpi.Px(250);
            int rowH = Dpi.Px(46);
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 2 * rowH + Dpi.Px(8)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = Dpi.Px(76)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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

        private Panel BuildParallelSubmenuPanel(PopupMenuRow parentRow, Action closeAll)
        {
            int width = Dpi.Px(190);
            int rowH = Dpi.Px(46);
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 6 * rowH + Dpi.Px(8)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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
                    settings.ParallelCount = count;
                    settings.Save();
                    parentRow.RightBadgeText = count + (count == 1 ? " Download   ◂" : " Downloads   ◂");
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    ProcessQueue();
                    if (closeAll != null) closeAll();
                };
                container.Controls.Add(row);
            }
            return container;
        }

        private Panel BuildTurboSubmenuPanel(PopupMenuRow parentRow, Action closeAll)
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
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = counts.Length * rowH + Dpi.Px(8)
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - Dpi.Px(1), container.Height - Dpi.Px(1));
                }
            };

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
                    settings.ConcurrentFragments = fragCount;
                    settings.Save();
                    parentRow.RightBadgeText = fragCount + "x   ◂";
                    parentRow.Invalidate();
                    selOptions.SetValueLabel(GetOptionsSummaryLabel());
                    if (closeAll != null) closeAll();
                };
                container.Controls.Add(row);
            }
            return container;
        }
    }
}
