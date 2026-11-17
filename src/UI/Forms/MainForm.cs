using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace Stash
{
    public partial class MainForm : ThemedForm
    {
        private AppSettings settings;

        // Top Flow Controls
        private PasteLinkHeroButton btnPasteHero;
        private CompactToggleSwitch toggleSmartMode;
        private InlineFlowSelector selDownloadFormat;
        private InlineFlowSelector selQuality;
        private InlineFlowSelector selSaveTo;
        private InlineFlowSelector selOptions;
        private InlineFlowSelector selBrowserCookies;

        // Queue & Status Bar Controls
        private ModernButton btnCancelSelected;
        private ModernButton btnRemoveFinished;
        private ModernButton btnPauseAll;
        private readonly QueueStore queueStore = new QueueStore(AppPaths.Current.QueuePath);
        private readonly TaskbarProgress taskbar = new TaskbarProgress();
        private readonly System.Threading.CancellationTokenSource engineCancellation = new System.Threading.CancellationTokenSource();
        private NotifyIcon trayIcon;
        private bool queueWasBusy;
        private bool clipboardListening;
        private string ignoreClipboardText;
        private string lastClipboardText;
        private bool engineOperationRunning;
        private ModernButton btnInformation;
        private readonly RecentLogWriter logWriter = new RecentLogWriter(Path.Combine(AppPaths.Current.DataDirectory, "logs"));
        private Label lblToolsStatus;
        private Label lblQueueStats;
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
            "Audio (FLAC)",
            "Audio (WAV)"
        };

        private static readonly string[] FormatPopupTitles = new string[]
        {
            "Video (MP4)",
            "Video (MKV)",
            "Audio (MP3)",
            "Audio (M4A)",
            "Audio (FLAC)",
            "Audio (WAV)"
        };

        private static readonly string[] FormatPopupRightTags = new string[]
        {
            "Best Compatibility",
            "Best Source Quality",
            "320 kbps",
            "Original AAC",
            "Lossless Audio",
            "16-bit PCM"
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
            AppLog.Connect(logWriter.WriteError);
            ConfigureTls();
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
            CheckToolsStatus();
            RestoreQueue();
            Shown += (sender, args) =>
            {
                updateService.CleanupStaleFiles();
                SweepTemporaryFoldersAsync();
                ScheduleAutomaticEngineCheck();
            };
        }

        // Prefer TLS 1.3 where the OS supports it, always allowing TLS 1.2.
        private static void ConfigureTls()
        {
            const SecurityProtocolType Tls13 = (SecurityProtocolType)12288;
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | Tls13; }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        }

        private void RestoreQueue()
        {
            List<DownloadQueueItem> restored = queueStore.Load();
            if (restored.Count == 0) return;
            downloadManager.AddRestoredItems(restored);
            queueListView.AppendItems(restored);
            UpdateQueueHeaderStats();
            int paused = restored.FindAll(item => item.Status == QueueItemStatus.Paused).Count;
            if (paused > 0)
                Shown += (s, e) => ShowNotice(paused == 1 ? "1 unfinished download was restored. Resume it when ready."
                    : paused + " unfinished downloads were restored. Use Resume All when ready.");
            // Refresh file sizes of finished items in the background.
            foreach (DownloadQueueItem item in restored)
            {
                if (item.Status != QueueItemStatus.Completed || string.IsNullOrEmpty(item.OutputFilePath)) continue;
                DownloadQueueItem current = item;
                CompletedDownloadDetails.ReadFileAsync(current.OutputFilePath, null).ContinueWith(task =>
                {
                    if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || IsDisposed) return;
                    current.CompletedDetails = task.Result;
                    queueListView.RefreshChangedItems();
                }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        private async void SweepTemporaryFoldersAsync()
        {
            List<string> folders = new List<string>(settings.RecentFolders);
            folders.Add(settings.CurrentOutputFolder);
            List<string> inUse = new List<string>();
            foreach (DownloadQueueItem item in downloadManager.GetItems())
            {
                folders.Add(item.OutputFolder);
                inUse.Add(item.TemporaryFolder);
            }
            try
            {
                int removed = await System.Threading.Tasks.Task.Run(() =>
                    StorageFileService.SweepOrphanedTemporaryFolders(folders, inUse, TimeSpan.FromHours(6)));
                if (removed > 0) logWriter.Write("Removed " + removed + " leftover temporary folder(s).");
            }
            catch (Exception ex) { AppLog.Error("Temporary folder sweep failed", ex); }
        }

        // Accept links dragged from a browser, text, or .url shortcut files.
        private void EnableLinkDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += (s, e) => e.Effect = ReadDroppedText(e.Data) != null ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
            control.DragDrop += (s, e) =>
            {
                string text = ReadDroppedText(e.Data);
                if (text == null) return;
                Activate();
                BeginInvoke((Action)(() => ProcessInputText(text, InputSource.Drop)));
            };
            foreach (Control child in control.Controls) EnableLinkDrop(child);
        }

        private static string ReadDroppedText(IDataObject data)
        {
            if (data == null) return null;
            try
            {
                if (data.GetDataPresent(DataFormats.UnicodeText)) return data.GetData(DataFormats.UnicodeText) as string;
                if (data.GetDataPresent(DataFormats.Text)) return data.GetData(DataFormats.Text) as string;
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files == null) return null;
                    List<string> links = new List<string>();
                    foreach (string file in files)
                    {
                        if (!string.Equals(Path.GetExtension(file), ".url", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (string line in File.ReadAllLines(file))
                            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) links.Add(line.Substring(4).Trim());
                    }
                    return links.Count == 0 ? null : string.Join("\n", links);
                }
            }
            catch (Exception ex) { AppLog.Error("Could not read dropped data", ex); }
            return null;
        }

        // Clipboard watching (opt-in): links copied in other apps are queued automatically.
        private const int WmClipboardUpdate = 0x031D;

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FlashInfo info);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }

        private void FlashWindow()
        {
            if (!IsHandleCreated || ContainsFocus) return;
            FlashInfo info = new FlashInfo { Window = Handle, Flags = 0x3 | 0xC, Count = 3 }; // FLASHW_ALL | FLASHW_TIMERNOFG
            info.Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(info);
            FlashWindowEx(ref info);
        }

        private void UpdateClipboardWatch()
        {
            if (!IsHandleCreated) return;
            if (settings.WatchClipboard && !clipboardListening)
            {
                clipboardListening = AddClipboardFormatListener(Handle);
                lastClipboardText = ReadClipboardText(); // Do not queue whatever was already copied.
            }
            else if (!settings.WatchClipboard && clipboardListening)
            {
                RemoveClipboardFormatListener(Handle);
                clipboardListening = false;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            clipboardListening = false;
            UpdateClipboardWatch();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WmClipboardUpdate && clipboardListening && settings.WatchClipboard && Enabled)
                BeginInvoke((Action)OnClipboardChanged);
        }

        private void OnClipboardChanged()
        {
            string text = ReadClipboardText();
            if (text == null || text.Length > 8000 || text == lastClipboardText) return;
            lastClipboardText = text;
            if (text == ignoreClipboardText) { ignoreClipboardText = null; return; }
            // Only react to copies that are links, not ordinary text that happens to contain one.
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            if (OwnedForms.Length > 0 || !Enabled) return; // A dialog is open.
            ProcessInputText(text, InputSource.ClipboardWatch);
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
            Font = Theme.GetFont("Segoe UI", 9.5F, FontStyle.Regular);
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
            appToolTip.SetToolTip(btnPasteHero, "Paste link from clipboard & download immediately (Ctrl+V). You can also drag links onto the window.");
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

            // Keep download options alongside the other download settings.
            selOptions = new InlineFlowSelector("", GetOptionsSummaryLabel())
            {
                Margin = new Padding(0),
                AccessibleName = "Download options",
                AccessibleRole = AccessibleRole.PushButton
            };
            appToolTip.SetToolTip(selOptions, "Download options");
            selOptions.Clicked += (s, e) => ShowOptionsNestedPopup();

            selBrowserCookies = new InlineFlowSelector("Browser Cookies", "")
            {
                Margin = new Padding(0),
                IconOnly = true,
                Dock = DockStyle.Right,
                AccessibleName = "Browser Cookies",
                AccessibleRole = AccessibleRole.PushButton
            };
            UpdateBrowserCookiesTooltip();
            selBrowserCookies.Clicked += (s, e) => ShowBrowserCookiesPopup();

            toggleSmartMode.CheckedChanged += (s, e) =>
            {
                bool on = toggleSmartMode.Checked;
                selDownloadFormat.Enabled = on;
                UpdateQualitySelectorEnabledState();
                selSaveTo.Enabled = on;
                selOptions.Enabled = on;
                selBrowserCookies.Enabled = on;
            };

            UpdateQualitySelectorEnabledState();

            flowTop.Controls.Add(btnPasteHero);
            flowTop.Controls.Add(selDownloadFormat);
            flowTop.Controls.Add(selQuality);
            flowTop.Controls.Add(selSaveTo);
            flowTop.Controls.Add(selOptions);

            pnlTopBar.Controls.Add(flowTop);
            pnlTopBar.Controls.Add(selBrowserCookies);

            // 2. Secondary Sub-Toolbar (Queue Stats + Engine Status + Action Buttons)
            TableLayoutPanel pnlQueueBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = Dpi.Px(44),
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(Dpi.Px(18), Dpi.Px(8), Dpi.Px(18), Dpi.Px(4)),
                BackColor = Theme.BgWindow
            };
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlQueueBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblQueueStats = new Label
            {
                Text = "QUEUE  •  0 ITEMS",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Theme.GetFont("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                ForeColor = Theme.TextMuted
            };

            lblToolsStatus = new Label
            {
                Text = "Checking engine...",
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Cursor = Cursors.Hand,
                Font = Theme.GetFont("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Margin = new Padding(0, 0, Dpi.Px(10), 0),
                ForeColor = Theme.AccentWarning
            };
            lblToolsStatus.Click += BtnInstallTools_Click;

            btnCancelSelected = new ModernButton
            {
                Text = "Cancel Selected",
                Size = new Size(Dpi.Px(115), Dpi.Px(27)),
                Margin = new Padding(0, 0, Dpi.Px(8), 0),
                BaseColor = Color.FromArgb(46, 24, 28),
                HoverColor = Color.FromArgb(64, 30, 36),
                ForeColor = Color.FromArgb(252, 165, 165),
                Font = Theme.GetFont("Segoe UI Semibold", 8.5F, FontStyle.Bold),
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
                Font = Theme.GetFont("Segoe UI", 8.5F),
                CornerRadius = Dpi.Px(6)
            };
            btnRemoveFinished.Click += BtnRemoveFinished_Click;
            appToolTip.SetToolTip(btnRemoveFinished, "Remove completed, cancelled and failed items from the list");
            appToolTip.SetToolTip(btnCancelSelected, "Cancel the selected download (Del)");
            btnCancelSelected.Enabled = false;

            btnPauseAll = new ModernButton
            {
                Text = "Pause All",
                Size = new Size(Dpi.Px(96), Dpi.Px(27)),
                Margin = new Padding(0, 0, Dpi.Px(8), 0),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = Theme.GetFont("Segoe UI", 8.5F),
                CornerRadius = Dpi.Px(6),
                Enabled = false
            };
            btnPauseAll.Click += BtnPauseAll_Click;
            appToolTip.SetToolTip(btnPauseAll, "Pause or resume every download in the queue");

            btnInformation = new InformationIconButton
            {
                Size = new Size(Dpi.Px(30), Dpi.Px(27)), AccessibleName = "About Stash and updates",
                Margin = new Padding(0), BaseColor = Theme.BgTopBar,
                HoverColor = Theme.BgSurfaceHover, BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary, Font = Theme.GetFont("Segoe UI Symbol", 12F), CornerRadius = Dpi.Px(6)
            };
            appToolTip.SetToolTip(btnInformation, "About Stash and updates");
            btnInformation.Click += async (s, e) => await ShowInformationAsync();

            pnlQueueBar.Controls.Add(lblQueueStats, 0, 0);
            pnlQueueBar.Controls.Add(btnPauseAll, 1, 0);
            pnlQueueBar.Controls.Add(btnCancelSelected, 2, 0);
            pnlQueueBar.Controls.Add(btnRemoveFinished, 3, 0);

            TableLayoutPanel statusBar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = Dpi.Px(38), ColumnCount = 4, RowCount = 1,
                Padding = new Padding(Dpi.Px(20), Dpi.Px(5), Dpi.Px(18), Dpi.Px(5)), BackColor = Theme.BgTopBar
            };
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblToolsStatus.Anchor = AnchorStyles.Left;
            Label versionLabel = new Label
            {
                Text = "v" + UpdateService.InstalledVersion, AutoSize = true, Anchor = AnchorStyles.Right,
                ForeColor = Theme.TextMuted, Font = Theme.GetFont("Segoe UI", 8F),
                Margin = new Padding(Dpi.Px(12), 0, Dpi.Px(8), 0)
            };
            btnInformation.Anchor = AnchorStyles.Right;
            statusBar.Controls.Add(lblToolsStatus, 0, 0);
            statusBar.Controls.Add(versionLabel, 2, 0);
            statusBar.Controls.Add(btnInformation, 3, 0);
            notice = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.TextSecondary, Visible = false, Margin = new Padding(Dpi.Px(12), 0, 0, 0) };
            statusBar.Controls.Add(notice, 1, 0);

            // 4. Center Custom Card Queue List
            Panel pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Dpi.Px(18), Dpi.Px(4), Dpi.Px(18), Dpi.Px(6)),
                BackColor = Theme.BgWindow
            };

            queueListView = new QueueListControl
            {
                Dock = DockStyle.Fill,
                ThumbnailDecoderPath = ffmpegPath
            };
            queueListView.PauseResumeRequested += TogglePauseResumeItem;
            queueListView.CancelRequested += CancelSingleItem;
            queueListView.DetailsRequested += item =>
            {
                using (DownloadDetailsDialog dialog = new DownloadDetailsDialog(item.Failure ?? DownloadFailure.Create("Downloading", "No engine error was provided."))) dialog.ShowDialog(this);
            };
            queueListView.RetryRequested += item => downloadManager.Retry(item, settings.BrowserCookies);
            queueListView.RemoveRequested += item =>
            {
                if (downloadManager.RemoveFinished(item)) queueListView.RemoveItem(item);
                UpdateQueueHeaderStats();
            };
            queueListView.OpenFileFolderRequested += OpenCompletedItemFolder;
            queueListView.OpenFileRequested += OpenCompletedFile;
            queueListView.CopyUrlRequested += CopyItemUrl;
            queueListView.SelectionChanged += (s, e) => UpdateSelectionButtons();
            pnlCenter.Controls.Add(queueListView);

            Controls.Add(pnlCenter);

            Controls.Add(statusBar);
            Controls.Add(pnlQueueBar);
            Controls.Add(pnlTopBar);
            EnableLinkDrop(this);
        }

    }
}
