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

        private readonly List<DownloadQueueItem> queue = new List<DownloadQueueItem>();
        private readonly object queueLock = new object();
        private int activeDownloads = 0;

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
            ApplyDarkTitleBar();
            CheckToolsStatus();
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

        private string GetOptionsSummaryLabel()
        {
            return "Options";
        }

        private void InitializeUi()
        {
            Text = "Stash";
            Size = new Size(1020, 660);
            MinimumSize = new Size(880, 520);
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
                Height = 66,
                Padding = new Padding(18, 13, 18, 13),
                BackColor = Theme.BgTopBar
            };
            pnlTopBar.Paint += (s, e) =>
            {
                using (Pen borderPen = new Pen(Theme.BorderSubtle, 1f))
                {
                    e.Graphics.DrawLine(borderPen, 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
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
                Margin = new Padding(0, 0, 6, 0)
            };
            appToolTip.SetToolTip(btnPasteHero, "Paste link from clipboard & download immediately (Ctrl+V)");
            btnPasteHero.Click += (s, e) => PasteFromClipboardAndDownload();

            // Smaller Minimalist Toggle Switch (No icon)
            toggleSmartMode = new CompactToggleSwitch
            {
                Checked = true,
                Margin = new Padding(0, 10, 10, 0)
            };
            appToolTip.SetToolTip(toggleSmartMode, "Toggle quick format & quality selectors");

            // "Download  Video ⌄"
            selDownloadFormat = new InlineFlowSelector("Download", FormatShortNames[settings.FormatIndex])
            {
                Margin = new Padding(0, 0, 8, 0)
            };
            selDownloadFormat.Clicked += (s, e) => ShowFormatPopup();

            // "Quality  Best ⌄"
            selQuality = new InlineFlowSelector("Quality", QualityShortNames[settings.QualityIndex])
            {
                Margin = new Padding(0, 0, 8, 0)
            };
            selQuality.Clicked += (s, e) => ShowQualityPopup();

            // "Save to  Downloads ⌄"
            selSaveTo = new InlineFlowSelector("Save to", GetFolderShortDisplayName(settings.CurrentOutputFolder))
            {
                Margin = new Padding(0, 0, 8, 0)
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
                Height = 44,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(18, 8, 18, 4),
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
                Margin = new Padding(0, 0, 10, 0),
                ForeColor = Theme.AccentWarning
            };
            lblToolsStatus.Click += BtnInstallTools_Click;

            btnInstallTools = new ModernButton
            {
                Text = "Install yt-dlp + FFmpeg",
                Size = new Size(165, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = 6
            };
            btnInstallTools.Click += BtnInstallTools_Click;

            btnCancelSelected = new ModernButton
            {
                Text = "Cancel Selected",
                Size = new Size(115, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Color.FromArgb(46, 24, 28),
                HoverColor = Color.FromArgb(64, 30, 36),
                ForeColor = Color.FromArgb(252, 165, 165),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                CornerRadius = 6
            };
            btnCancelSelected.Click += BtnCancelSelected_Click;

            btnRemoveFinished = new ModernButton
            {
                Text = "Clear Finished",
                Size = new Size(106, 27),
                Margin = new Padding(0, 0, 8, 0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = 6
            };
            btnRemoveFinished.Click += BtnRemoveFinished_Click;

            btnToggleLog = new ModernButton
            {
                Text = "Console",
                Size = new Size(98, 27),
                Margin = new Padding(0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 8.5F),
                CornerRadius = 6
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
                Dock = DockStyle.Bottom, Height = 38, ColumnCount = 5, RowCount = 1,
                Padding = new Padding(20, 5, 18, 5), BackColor = Theme.BgTopBar
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
                Height = 125,
                Visible = false,
                Padding = new Padding(18, 6, 18, 14),
                BackColor = Theme.BgWindow
            };

            CardPanel cardLog = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                CardFillColor = Color.FromArgb(14, 15, 19),
                CardBorderColor = Theme.BorderSubtle,
                CornerRadius = 8
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
                Padding = new Padding(18, 4, 18, 6),
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
            popup.Show(ownerSelector, new Point(0, ownerSelector.Height + 2));
            return popup;
        }

        // Perfectly aligned Download Format dropdown with fixed left checkmark column and right-aligned detail tag
        private void ShowFormatPopup()
        {
            int width = 265;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = FormatPopupTitles.Length * 34 + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < FormatPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.FormatIndex);
                PopupMenuRow row = new PopupMenuRow(FormatPopupTitles[idx], FormatPopupRightTags[idx], null, active)
                {
                    Location = new Point(4, 4 + idx * 34),
                    Width = width - 8
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
            int width = 245;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = QualityPopupTitles.Length * 34 + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            ToolStripDropDown popup = null;
            for (int i = 0; i < QualityPopupTitles.Length; i++)
            {
                int idx = i;
                bool active = (idx == settings.QualityIndex);
                PopupMenuRow row = new PopupMenuRow(QualityPopupTitles[idx], QualityPopupRightTags[idx], null, active)
                {
                    Location = new Point(4, 4 + idx * 34),
                    Width = width - 8
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
            int width = 325;
            int itemCount = settings.RecentFolders.Count;
            int rowH = 46;
            int totalHeight = (itemCount * rowH) + 46;

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
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                    int sepY = itemCount * rowH + 6;
                    e.Graphics.DrawLine(p, 8, sepY, container.Width - 8, sepY);
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
                    Location = new Point(4, 4 + i * rowH),
                    Width = width - 8,
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
                Location = new Point(4, itemCount * rowH + 9),
                Width = width - 8,
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

        // Nested Dropdown containing "Embed", "Parallel", and "Turbo" submenus inside a single Region-shaped popup
        private void ShowOptionsNestedPopup()
        {
            int leftWidth = 225;
            int leftHeight = 3 * 34 + 8; // 110
            int gap = 4;
            int rightMaxWidth = 235;
            int totalWidth = leftWidth + gap + rightMaxWidth;
            int totalHeight = 34 + (6 * 46 + 8) + 8; // Enough height for 6 two-line rows in Parallel & 5 in Turbo

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
                    e.Graphics.DrawRectangle(p, 0, 0, pnlLeft.Width - 1, pnlLeft.Height - 1);
                }
            };

            Panel pnlRightHost = new Panel
            {
                BackColor = Theme.BgSurface,
                Location = new Point(0, 0),
                Width = rightMaxWidth,
                Height = 76,
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

            PopupMenuRow rowEmbed = new PopupMenuRow(
                "Embed",
                GetEmbedShortDisplayName() + "   ◂",
                null,
                false,
                "")
            {
                Location = new Point(4, 4),
                Width = leftWidth - 8
            };

            PopupMenuRow rowParallel = new PopupMenuRow(
                "Parallel",
                settings.ParallelCount + (settings.ParallelCount == 1 ? " Download   ◂" : " Downloads   ◂"),
                null,
                false,
                "")
            {
                Location = new Point(4, 38),
                Width = leftWidth - 8
            };

            PopupMenuRow rowTurbo = new PopupMenuRow(
                "Turbo",
                settings.ConcurrentFragments + "x   ◂",
                null,
                false,
                "")
            {
                Location = new Point(4, 72),
                Width = leftWidth - 8
            };

            int currentSubmenuIndex = -1;

            Action<int, PopupMenuRow, Panel, int> showSubmenu = (index, ownerRow, childPanel, alignY) =>
            {
                if (currentSubmenuIndex == index && pnlRightHost.Visible) return;
                currentSubmenuIndex = index;

                rowEmbed.IsSelected = (ownerRow == rowEmbed);
                rowParallel.IsSelected = (ownerRow == rowParallel);
                rowTurbo.IsSelected = (ownerRow == rowTurbo);
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

            Action openEmbed = () =>
            {
                Panel sub = BuildEmbedSubmenuPanel(rowEmbed);
                showSubmenu(0, rowEmbed, sub, 0);
            };

            Action openParallel = () =>
            {
                Panel sub = BuildParallelSubmenuPanel(rowParallel, () => popup.Close());
                showSubmenu(1, rowParallel, sub, 34);
            };

            Action openTurbo = () =>
            {
                Panel sub = BuildTurboSubmenuPanel(rowTurbo, () => popup.Close());
                showSubmenu(2, rowTurbo, sub, 68);
            };

            rowEmbed.MouseEnter += (s, e) => openEmbed();
            rowEmbed.MouseDown += (s, e) => openEmbed();

            rowParallel.MouseEnter += (s, e) => openParallel();
            rowParallel.MouseDown += (s, e) => openParallel();

            rowTurbo.MouseEnter += (s, e) => openTurbo();
            rowTurbo.MouseDown += (s, e) => openTurbo();

            pnlLeft.Controls.Add(rowEmbed);
            pnlLeft.Controls.Add(rowParallel);
            pnlLeft.Controls.Add(rowTurbo);

            rootContainer.Controls.Add(pnlLeft);
            rootContainer.Controls.Add(pnlRightHost);

            updatePopupRegion();

            popup.Closed += (s, e) => selOptions.SetOpenState(false);
            selOptions.SetOpenState(true);
            popup.Show(selOptions, new Point(selOptions.Width - popup.Width, selOptions.Height + 2));
        }

        private Panel BuildEmbedSubmenuPanel(PopupMenuRow parentRow)
        {
            int width = 235;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 76
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            PopupMenuRow rowMeta = new PopupMenuRow(
                "Metadata & Chapters",
                null,
                null,
                settings.EmbedMetadata,
                settings.EmbedMetadata ? "☑" : "☐")
            {
                Location = new Point(4, 4),
                Width = width - 8
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
                Location = new Point(4, 38),
                Width = width - 8
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
            int width = 190;
            int rowH = 46;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = 6 * rowH + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            for (int n = 1; n <= 6; n++)
            {
                int count = n;
                bool active = (count == settings.ParallelCount);
                string title = count + (count == 1 ? " Download" : " Downloads");
                PopupMenuRow row = new PopupMenuRow(title, null, "Concurrent", active)
                {
                    Location = new Point(4, 4 + (n - 1) * rowH),
                    Width = width - 8
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

            int width = 210;
            int rowH = 46;
            Panel container = new Panel
            {
                BackColor = Theme.BgSurface,
                Width = width,
                Height = counts.Length * rowH + 8
            };
            container.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Theme.BorderHover, 1f))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, container.Width - 1, container.Height - 1);
                }
            };

            for (int i = 0; i < counts.Length; i++)
            {
                int fragCount = counts[i];
                bool active = (fragCount == settings.ConcurrentFragments);
                PopupMenuRow row = new PopupMenuRow(titles[i], null, subTags[i], active)
                {
                    Location = new Point(4, 4 + i * rowH),
                    Width = width - 8
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
