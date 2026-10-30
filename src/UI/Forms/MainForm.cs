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
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private AppSettings settings;

        // Top Flow Controls
        private PasteLinkHeroButton btnPasteHero;
        private CompactToggleSwitch toggleSmartMode;
        private InlineFlowSelector selDownloadFormat;
        private InlineFlowSelector selQuality;
        private InlineFlowSelector selSaveTo;
        private InlineFlowSelector selOptions;

        // Queue & Status Bar Controls
        private ModernButton btnCancelSelected;
        private ModernButton btnRemoveFinished;
        private ModernButton btnInstallTools;
        private ModernButton btnToggleLog;
        private Label lblToolsStatus;
        private Label lblQueueStats;
        private Label lblActiveDownloads;
        private QueueListControl queueListView;
        private RichTextBox txtLog;
        private Panel pnlBottomLog;
        private ToolTip appToolTip;

        private DownloadManager downloadManager;
        private bool shutdownComplete;

        private string binFolder;
        private string ytDlpPath;
        private string ffmpegPath;

        private static readonly string[] FormatShortNames = new string[]
        {
            "Video",
            "Video (MKV)",
            "Audio (MP3)",
            "Audio (M4A)",
            "Audio (FLAC)"
        };

        private static readonly string[] FormatPopupTitles = new string[]
        {
            "Video (MP4)",
            "Video (MKV)",
            "Audio (MP3)",
            "Audio (M4A)",
            "Audio (FLAC)"
        };

        private static readonly string[] FormatPopupRightTags = new string[]
        {
            "Best Compatibility",
            "4K / 8K Lossless",
            "320 kbps",
            "Original AAC",
            "Lossless Audio"
        };

        private static readonly string[] QualityShortNames = new string[]
        {
            "Best",
            "4K (2160p)",
            "2K (1440p)",
            "1080p",
            "720p",
            "480p",
            "360p"
        };

        private static readonly string[] QualityPopupTitles = new string[]
        {
            "Best",
            "4K UHD",
            "2K QHD",
            "Full HD",
            "HD",
            "SD",
            "Data Saver"
        };

        private static readonly string[] QualityPopupRightTags = new string[]
        {
            "Max Source Quality",
            "2160p",
            "1440p",
            "1080p",
            "720p",
            "480p",
            "360p"
        };

        public MainForm()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            KeyPreview = true;
            settings = AppSettings.Load();
            InitializePaths();
            InitializeUi();
            downloadManager = new DownloadManager(new DownloadProcessRunner(), CheckAndPromptForDuplicateRename);
            downloadManager.Changed += RefreshQueueUi;
            downloadManager.Log += AppendLog;
            FitInitialWindowToScreen();
            ApplyDarkTitleBar();
            CheckToolsStatus();
        }

        private void FitInitialWindowToScreen()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int availableWidth = Math.Max(1, area.Width - Dpi.Px(16));
            int availableHeight = Math.Max(1, area.Height - Dpi.Px(16));
            MinimumSize = new Size(Math.Min(MinimumSize.Width, availableWidth),
                                   Math.Min(MinimumSize.Height, availableHeight));
            Size = new Size(Math.Min(Width, availableWidth), Math.Min(Height, availableHeight));
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            try
            {
                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                }
            }
            catch { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                e.SuppressKeyPress = true;
                PasteFromClipboardAndDownload();
            }
        }

        private void InitializePaths()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            binFolder = Path.Combine(appDir, "bin");
            if (!Directory.Exists(binFolder))
            {
                Directory.CreateDirectory(binFolder);
            }

            ytDlpPath = ExternalToolService.FindExecutable(binFolder, "yt-dlp.exe");
            ffmpegPath = ExternalToolService.FindExecutable(binFolder, "ffmpeg.exe");
        }

        private string GetFolderShortDisplayName(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return "Downloads";
            string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(leaf)) return trimmed;
            if (leaf.Length > 20) return leaf.Substring(0, 18) + "…";
            return leaf;
        }

        private string GetEmbedShortDisplayName()
        {
            if (settings.EmbedMetadata && settings.EmbedThumbnail) return "Tags + Art";
            if (settings.EmbedMetadata) return "Metadata";
            if (settings.EmbedThumbnail) return "Thumbnail";
            return "None";
        }

        private string GetSubtitlesShortDisplayName()
        {
            if (settings.EmbedSubtitles && settings.IncludeAutoCaptions) return "On + Auto";
            if (settings.EmbedSubtitles) return "On";
            if (settings.IncludeAutoCaptions) return "Auto Only";
            return "Off";
        }

        private string GetOptionsSummaryLabel()
        {
            return "Options";
        }

        private void InitializeUi()
        {
            // Geometry is explicitly converted from 96-DPI units by Dpi.Px.
            // Disable WinForms' additional automatic pass to avoid double scaling.
            AutoScaleMode = AutoScaleMode.None;
            Text = "Stash";
            Size = new Size(Dpi.Px(1020), Dpi.Px(660));
            MinimumSize = new Size(Dpi.Px(880), Dpi.Px(520));
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            BackColor = Theme.BgWindow;
            ForeColor = Theme.TextPrimary;
            ShowIcon = true;
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch { }

            appToolTip = new ToolTip();

            // 1. Top Command Flow Bar
            Panel pnlTopBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi.Px(66),
                Padding = new Padding(Dpi.Px(18), Dpi.Px(13), Dpi.Px(18), Dpi.Px(13)),
                BackColor = Theme.BgTopBar
            };
            pnlTopBar.Paint += (s, e) =>
            {
                using (Pen borderPen = new Pen(Theme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(borderPen, 0, pnlTopBar.Height - Dpi.Px(1), pnlTopBar.Width, pnlTopBar.Height - Dpi.Px(1));
                }
            };

            FlowLayoutPanel flowTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true,
                AutoSize = false,
                BackColor = Theme.BgTopBar
            };
            flowTop.Layout += (s, e) =>
            {
                int desiredHeight = flowTop.GetPreferredSize(new Size(flowTop.ClientSize.Width, 0)).Height
                    + pnlTopBar.Padding.Vertical;
                if (pnlTopBar.Height != desiredHeight) pnlTopBar.Height = desiredHeight;
            };

            // Purple "[Paste Link]" Hero Button
            btnPasteHero = new PasteLinkHeroButton
            {
                Margin = new Padding(0, 0, Dpi.Px(6), 0)
            };
            appToolTip.SetToolTip(btnPasteHero, "Paste link from clipboard & download immediately (Ctrl+V)");
            btnPasteHero.Click += (s, e) => PasteFromClipboardAndDownload();

            // Smaller Minimalist Toggle Switch (No icon)
            toggleSmartMode = new CompactToggleSwitch
            {
                Checked = true,
                Margin = new Padding(0, Dpi.Px(10), Dpi.Px(10), 0)
            };
            appToolTip.SetToolTip(toggleSmartMode, "Toggle quick format & quality selectors");

            // "Download  Video ⌄"
            selDownloadFormat = new InlineFlowSelector("Download", FormatShortNames[settings.FormatIndex])
            {
                Margin = new Padding(0, 0, Dpi.Px(8), 0)
            };
            selDownloadFormat.Clicked += (s, e) => ShowFormatPopup();

            // "Quality  Best ⌄"
            selQuality = new InlineFlowSelector("Quality", QualityShortNames[settings.QualityIndex])
            {
                Margin = new Padding(0, 0, Dpi.Px(8), 0)
            };
            selQuality.Clicked += (s, e) => ShowQualityPopup();

            // "Save to  Downloads ⌄"
            selSaveTo = new InlineFlowSelector("Save to", GetFolderShortDisplayName(settings.CurrentOutputFolder))
            {
                Margin = new Padding(0, 0, Dpi.Px(8), 0)
            };
            appToolTip.SetToolTip(selSaveTo, settings.CurrentOutputFolder);
            selSaveTo.Clicked += (s, e) => ShowSaveToHistoryPopup();

            // Combined Nested Dropdown for "Embed", "Parallel", and "Turbo"
            selOptions = new InlineFlowSelector("", GetOptionsSummaryLabel())
            {
                Margin = new Padding(0),
                IconOnly = true,
                Dock = DockStyle.Right,
                AccessibleName = "Download options",
                AccessibleRole = AccessibleRole.PushButton
            };
            appToolTip.SetToolTip(selOptions, "Download options");
            selOptions.Clicked += (s, e) => ShowOptionsNestedPopup();

            toggleSmartMode.CheckedChanged += (s, e) =>
            {
                bool on = toggleSmartMode.Checked;
                selDownloadFormat.Enabled = on;
                UpdateQualitySelectorEnabledState();
                selSaveTo.Enabled = on;
                selOptions.Enabled = on;
            };

            UpdateQualitySelectorEnabledState();

            flowTop.Controls.Add(btnPasteHero);
            flowTop.Controls.Add(selDownloadFormat);
            flowTop.Controls.Add(selQuality);
            flowTop.Controls.Add(selSaveTo);

            pnlTopBar.Controls.Add(flowTop);
            pnlTopBar.Controls.Add(selOptions);

            // 2. Secondary Sub-Toolbar (Queue Stats + Engine Status + Action Buttons)
            TableLayoutPanel pnlQueueBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = Dpi.Px(44),
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(Dpi.Px(18), Dpi.Px(8), Dpi.Px(18), Dpi.Px(4)),
                BackColor = Theme.BgWindow
            };
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblQueueStats = new Label
            {
                Text = "QUEUE  •  0 ITEMS",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                ForeColor = Theme.TextMuted
            };

            lblToolsStatus = new Label
            {
                Text = "Checking engine...",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, Dpi.Px(10), 0),
                ForeColor = Theme.AccentWarning
            };
            lblToolsStatus.Click += BtnInstallTools_Click;

            btnInstallTools = new ModernButton
            {
                Text = "Install yt-dlp + FFmpeg",
                Size = new Size(Dpi.Px(165), Dpi.Px(27)),
                Margin = new Padding(0, 0, Dpi.Px(8), 0),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = Dpi.Px(6)
            };
            btnInstallTools.Click += BtnInstallTools_Click;

            btnCancelSelected = new ModernButton
            {
                Text = "Cancel Selected",
                Size = new Size(Dpi.Px(115), Dpi.Px(27)),
                Margin = new Padding(0, 0, Dpi.Px(8), 0),
                BaseColor = Color.FromArgb(46, 24, 28),
                HoverColor = Color.FromArgb(64, 30, 36),
                ForeColor = Color.FromArgb(252, 165, 165),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = Dpi.Px(6)
            };
            btnCancelSelected.Click += BtnCancelSelected_Click;

            btnRemoveFinished = new ModernButton
            {
                Text = "Clear Finished",
                Size = new Size(Dpi.Px(106), Dpi.Px(27)),
                Margin = new Padding(0, 0, Dpi.Px(8), 0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = Dpi.Px(6)
            };
            btnRemoveFinished.Click += BtnRemoveFinished_Click;

            btnToggleLog = new ModernButton
            {
                Text = "Console",
                Size = new Size(Dpi.Px(98), Dpi.Px(27)),
                Margin = new Padding(0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = Dpi.Px(6)
            };
            btnToggleLog.Click += (s, e) =>
            {
                pnlBottomLog.Visible = !pnlBottomLog.Visible;
                btnToggleLog.Text = pnlBottomLog.Visible ? "Hide Console" : "Console";
            };

            pnlQueueBar.Controls.Add(lblQueueStats, 0, 0);
            pnlQueueBar.Controls.Add(btnCancelSelected, 1, 0);
            pnlQueueBar.Controls.Add(btnRemoveFinished, 2, 0);

            TableLayoutPanel statusBar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = Dpi.Px(38), ColumnCount = 5, RowCount = 1,
                Padding = new Padding(Dpi.Px(20), Dpi.Px(5), Dpi.Px(18), Dpi.Px(5)), BackColor = Theme.BgTopBar
            };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblToolsStatus.Anchor = AnchorStyles.Left;
            lblActiveDownloads = new Label
            {
                Text = "·  0 active downloads", AutoSize = true, Anchor = AnchorStyles.Left,
                ForeColor = Theme.TextSecondary, Font = new Font("Segoe UI", 8.5F),
                Margin = new Padding(0)
            };
            btnInstallTools.Anchor = AnchorStyles.Right;
            btnToggleLog.Anchor = AnchorStyles.Right;
            btnInstallTools.BaseColor = Theme.BgTopBar;
            btnToggleLog.BaseColor = Theme.BgTopBar;
            btnToggleLog.BorderColor = Color.Empty;
            statusBar.Controls.Add(lblToolsStatus, 0, 0);
            statusBar.Controls.Add(lblActiveDownloads, 1, 0);
            statusBar.Controls.Add(btnInstallTools, 3, 0);
            statusBar.Controls.Add(btnToggleLog, 4, 0);

            // 3. Collapsible Bottom Console Log Card
            pnlBottomLog = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = Dpi.Px(125),
                Visible = false,
                Padding = new Padding(Dpi.Px(18), Dpi.Px(6), Dpi.Px(18), Dpi.Px(14)),
                BackColor = Theme.BgWindow
            };

            CardPanel cardLog = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Dpi.Px(10), Dpi.Px(8), Dpi.Px(10), Dpi.Px(8)),
                CardFillColor = Color.FromArgb(14, 15, 19),
                CardBorderColor = Theme.BorderSubtle,
                CornerRadius = Dpi.Px(8)
            };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(14, 15, 19),
                ForeColor = Color.FromArgb(148, 163, 184),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.8F)
            };
            cardLog.Controls.Add(new DarkConsoleScrollHost(txtLog));
            pnlBottomLog.Controls.Add(cardLog);

            // 4. Center Custom Card Queue List
            Panel pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Dpi.Px(18), Dpi.Px(4), Dpi.Px(18), Dpi.Px(6)),
                BackColor = Theme.BgWindow
            };

            queueListView = new QueueListControl
            {
                Dock = DockStyle.Fill
            };
            queueListView.PauseResumeRequested += TogglePauseResumeItem;
            queueListView.CancelRequested += CancelSingleItem;
            queueListView.OpenFileFolderRequested += OpenCompletedItemFolder;
            pnlCenter.Controls.Add(queueListView);

            Controls.Add(pnlCenter);
            Controls.Add(pnlBottomLog);
            Controls.Add(statusBar);
            Controls.Add(pnlQueueBar);
            Controls.Add(pnlTopBar);
        }

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
