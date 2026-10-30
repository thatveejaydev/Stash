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
        private readonly RecentLogWriter logWriter = new RecentLogWriter(Path.Combine(AppPaths.Current.DataDirectory, "logs"));
        private Label lblToolsStatus;
        private Label lblQueueStats;
        private Label lblActiveDownloads;
        private QueueListControl queueListView;


        private ToolTip appToolTip;

        private DownloadManager downloadManager;
        private bool shutdownComplete;
        private readonly Timer progressRefreshTimer = new Timer { Interval = 200 };
        private bool progressRefreshPending;
        private readonly Timer noticeTimer = new Timer { Interval = 5000 };
        private Label notice;

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
            downloadManager.ProgressChanged += () =>
            {
                progressRefreshPending = true;
                progressRefreshTimer.Start();
            };
            progressRefreshTimer.Tick += (sender, args) =>
            {
                progressRefreshTimer.Stop();
                if (!progressRefreshPending) return;
                progressRefreshPending = false;
                queueListView.RefreshChangedItems();
            };
            downloadManager.Log += AppendLog;
            downloadManager.ErrorLog += logWriter.WriteError;
            noticeTimer.Tick += (sender, args) => { noticeTimer.Stop(); notice.Visible = false; };
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
            binFolder = AppPaths.Current.EnginesDirectory;
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
            btnInstallTools.BaseColor = Theme.BgTopBar;
            statusBar.Controls.Add(lblToolsStatus, 0, 0);
            statusBar.Controls.Add(lblActiveDownloads, 1, 0);
            statusBar.Controls.Add(btnInstallTools, 3, 0);
            notice = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextSecondary, Visible = false, Margin = new Padding(Dpi.Px(12), 0, 0, 0) };
            statusBar.Controls.Add(notice, 2, 0);

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
            queueListView.DetailsRequested += item =>
            {
                using (DownloadDetailsDialog dialog = new DownloadDetailsDialog(item.Failure ?? DownloadFailure.Create("Downloading", "No engine error was provided."))) dialog.ShowDialog(this);
            };
            queueListView.RetryRequested += item => downloadManager.Retry(item);
            queueListView.RemoveRequested += item =>
            {
                if (downloadManager.RemoveFinished(item)) queueListView.RemoveItem(item);
                UpdateQueueHeaderStats();
            };
            queueListView.OpenFileFolderRequested += OpenCompletedItemFolder;
            pnlCenter.Controls.Add(queueListView);

            Controls.Add(pnlCenter);

            Controls.Add(statusBar);
            Controls.Add(pnlQueueBar);
            Controls.Add(pnlTopBar);
        }

    }
}
