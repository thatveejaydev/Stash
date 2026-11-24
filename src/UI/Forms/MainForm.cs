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

        // Header, queue and footer
        private LinkInputBar linkBar;
        private QueueListControl queueListView;
        private Label lblToolsStatus;
        private Label lblQueueSummary;
        private IconButton btnCookies;
        private IconButton btnInformation;
        private Label notice;
        private ToolTip appToolTip;

        private readonly QueueStore queueStore = new QueueStore(AppPaths.Current.QueuePath);
        private readonly TaskbarProgress taskbar = new TaskbarProgress();
        private readonly System.Threading.CancellationTokenSource engineCancellation = new System.Threading.CancellationTokenSource();
        private readonly RecentLogWriter logWriter = new RecentLogWriter(Path.Combine(AppPaths.Current.DataDirectory, "logs"));
        private NotifyIcon trayIcon;
        private bool queueWasBusy;
        private bool clipboardListening;
        private string ignoreClipboardText;
        private string lastClipboardText;
        private bool engineOperationRunning;

        private DownloadManager downloadManager;
        private bool shutdownComplete;
        private readonly Timer progressRefreshTimer = new Timer { Interval = 200 };
        private bool progressRefreshPending;
        private readonly Timer noticeTimer = new Timer { Interval = 5000 };

        private string binFolder;
        private string ytDlpPath;
        private string ffmpegPath;

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
                if (item.Status != QueueItemStatus.Completed || item.Mode == DownloadMode.TranscriptOnly ||
                    string.IsNullOrEmpty(item.OutputFilePath)) continue;
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
            control.DragEnter += (s, e) =>
            {
                bool valid = ReadDroppedText(e.Data) != null;
                e.Effect = valid ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None;
                queueListView.DropHighlight = valid;
            };
            control.DragLeave += (s, e) => queueListView.DropHighlight = false;
            control.DragDrop += (s, e) =>
            {
                queueListView.DropHighlight = false;
                string text = ReadDroppedText(e.Data);
                if (text == null) return;
                Activate();
                BeginInvoke((Action)(() => linkBar.Stage(text, false)));
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
            if (linkBar == null) return;
            if (e.KeyCode == Keys.L && e.Control)
            {
                e.SuppressKeyPress = true;
                linkBar.FocusInput();
                return;
            }
            if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                e.SuppressKeyPress = true;
                // In the link field, paste like a normal text box; anywhere else, download right away.
                if (linkBar.InputFocused) StageClipboardLinks(true);
                else DownloadClipboardLinks();
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

        private void InitializeUi()
        {
            // Geometry is explicitly converted from 96-DPI units by Dpi.Px.
            // Disable WinForms' additional automatic pass to avoid double scaling.
            AutoScaleMode = AutoScaleMode.None;
            Text = "Stash";
            Size = new Size(Dpi.Px(980), Dpi.Px(640));
            MinimumSize = new Size(Dpi.Px(720), Dpi.Px(460));
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.GetFont("Segoe UI", 9.5F);
            ShowIcon = true;
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null) Icon = exeIcon;
            }
            catch (Exception ex) { AppLog.Error("Could not load the window icon", ex); }

            appToolTip = new ToolTip();

            // 1. Header: the link, mode and its relevant settings.
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = Dpi.Px(100),
                Padding = new Padding(Dpi.Px(20), Dpi.Px(12), Dpi.Px(20), 0),
                BackColor = Theme.BgTopBar
            };
            header.Paint += (s, e) =>
            {
                using (Pen border = new Pen(Theme.BorderSubtle))
                    e.Graphics.DrawLine(border, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            linkBar = new LinkInputBar { Dock = DockStyle.Top };
            AcceptButton = linkBar.DownloadAction;
            linkBar.SubmitRequested += SubmitLink;
            linkBar.LinkChanged += (s, e) => RefreshCaptionPreview();
            linkBar.ModeChanged += kind =>
            {
                settings.SelectedKind = kind;
                settings.Save();
                UpdateHeaderSettings();
                RefreshCaptionPreview();
            };
            linkBar.FormatAnchor.Click += (s, e) => ShowPrimaryChoice();
            linkBar.DetailAnchor.Click += (s, e) => ShowDetailChoice();
            linkBar.FolderAnchor.Click += (s, e) => ShowFolderPopup(linkBar.FolderAnchor);
            linkBar.OptionsAnchor.Click += (s, e) => ShowSettingsPopup(linkBar.OptionsAnchor);
            appToolTip.SetToolTip(linkBar.OptionsAnchor, "More options");
            linkBar.SizeChanged += (s, e) => header.Height = header.Padding.Top + linkBar.Height + Dpi.Px(12);
            header.Controls.Add(linkBar);

            // 2. Queue
            Panel pnlCenter = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(Dpi.Px(20), Dpi.Px(10), Dpi.Px(20), Dpi.Px(6)),
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
                using (DownloadDetailsDialog dialog = new DownloadDetailsDialog(item.Failure ?? DownloadFailure.Create("Downloading", "No engine error was provided.")))
                    dialog.ShowDialog(this);
            };
            queueListView.RetryRequested += item => { downloadManager.Retry(item, settings.BrowserCookies); ProcessQueue(); };
            queueListView.RemoveRequested += item =>
            {
                if (downloadManager.RemoveFinished(item)) queueListView.RemoveItem(item);
                UpdateQueueHeaderStats();
            };
            queueListView.OpenFileFolderRequested += OpenCompletedItemFolder;
            queueListView.OpenFileRequested += OpenCompletedFile;
            queueListView.CopyUrlRequested += CopyItemUrl;
            queueListView.RenameRequested += RenameCompletedFile;
            queueListView.SectionActionRequested += HandleSectionAction;
            pnlCenter.Controls.Add(queueListView);

            // 3. Footer: queue summary or engine status, notices, and quiet icon buttons.
            TableLayoutPanel footer = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = Dpi.Px(40),
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(Dpi.Px(20), Dpi.Px(4), Dpi.Px(14), Dpi.Px(4)),
                BackColor = Theme.BgWindow
            };
            footer.Paint += (s, e) =>
            {
                using (Pen border = new Pen(Theme.BorderSubtle)) e.Graphics.DrawLine(border, 0, 0, footer.Width, 0);
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            lblToolsStatus = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Cursor = Cursors.Hand,
                Font = Theme.GetFont("Segoe UI Semibold", 8.75F),
                ForeColor = Theme.AccentWarning,
                Margin = new Padding(0, 0, Dpi.Px(14), 0),
                Visible = false,
                UseMnemonic = false
            };
            lblToolsStatus.Click += BtnInstallTools_Click;

            lblQueueSummary = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Font = Theme.GetFont("Segoe UI", 8.75F),
                ForeColor = Theme.TextMuted,
                Margin = Padding.Empty,
                UseMnemonic = false
            };

            notice = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = Theme.TextSecondary,
                Font = Theme.GetFont("Segoe UI", 8.75F),
                Visible = false,
                Margin = new Padding(Dpi.Px(14), 0, 0, 0),
                UseMnemonic = false
            };

            btnCookies = CreateFooterButton(IconKind.Cookie, "Browser cookies");
            btnCookies.Click += (s, e) => ShowCookiesPopup();
            btnInformation = CreateFooterButton(IconKind.Info, "About Stash, updates and download engines");
            btnInformation.Click += async (s, e) => await ShowInformationAsync();

            footer.Controls.Add(lblToolsStatus, 0, 0);
            footer.Controls.Add(lblQueueSummary, 1, 0);
            footer.Controls.Add(notice, 2, 0);
            footer.Controls.Add(btnCookies, 3, 0);
            footer.Controls.Add(btnInformation, 4, 0);

            Controls.Add(pnlCenter);
            Controls.Add(footer);
            Controls.Add(header);
            UpdateHeaderSettings();
            RefreshCaptionPreview();
            UpdateBrowserCookiesTooltip();
            EnableLinkDrop(this);
        }

        private IconButton CreateFooterButton(IconKind icon, string tooltip)
        {
            IconButton button = new IconButton(icon, tooltip) { Margin = new Padding(Dpi.Px(2), 0, 0, 0), Anchor = AnchorStyles.Right };
            appToolTip.SetToolTip(button, tooltip);
            return button;
        }

    }
}
