using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MediaDownloader
{
    public enum PlaylistDownloadChoice
    {
        EntirePlaylist,
        SingleVideoOnly,
        CustomRange
    }

    public class PlaylistPromptDialog : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly string playlistUrl;
        private readonly string ytDlpPath;
        private PlaylistInfo playlistInfo;

        private readonly bool hasSingleVideo;
        private readonly string singleVideoUrl;
        private readonly string singleVideoId;

        public List<string> ResultUrls { get; private set; }
        public List<string> ResultTitles { get; private set; }

        private PlaylistDownloadChoice selectedChoice;

        private Label lblTitle;
        private Label lblPlaylistInfo;
        private Label lblStatus;
        private ModernButton btnQueue;
        private ModernButton btnCancel;

        private PlaylistChoiceCard cardEntire;
        private PlaylistChoiceCard cardSingle;
        private PlaylistChoiceCard cardRange;

        private ModernNumericStepper stepperFrom;
        private ModernNumericStepper stepperTo;
        private Label lblFrom;
        private Label lblTo;
        private Label lblRangeHint;
        private Label lblRangeCount;
        private Panel pnlRangeInputs;

        public PlaylistPromptDialog(string url, string ytDlpExePath)
        {
            playlistUrl = url;
            ytDlpPath = ytDlpExePath;
            ResultUrls = new List<string>();
            ResultTitles = new List<string>();

            hasSingleVideo = DownloadUrlValidator.TryExtractSingleVideoUrl(url, out singleVideoUrl, out singleVideoId);
            selectedChoice = PlaylistDownloadChoice.EntirePlaylist;

            Text = "Stash \u2014 Playlist Detected";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.Px(680), Dpi.Px(385));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.BgTopBar;
            ForeColor = Theme.TextPrimary;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

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

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Icon Box
            Panel iconBox = new Panel
            {
                Location = new Point(Dpi.Px(20), Dpi.Px(18)),
                Size = new Size(Dpi.Px(40), Dpi.Px(40))
            };
            iconBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Theme.CreateRoundedRectangle(new Rectangle(0, 0, Dpi.Px(39), Dpi.Px(39)), 11))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(48, 38, 70)))
                using (Pen pen = new Pen(Theme.BrandPurpleLight, 2f))
                using (SolidBrush tri = new SolidBrush(Theme.BrandPurpleLight))
                {
                    e.Graphics.FillPath(fill, path);
                    // 3 playlist lines
                    e.Graphics.DrawLine(pen, Dpi.Px(11), Dpi.Px(13), Dpi.Px(22), Dpi.Px(13));
                    e.Graphics.DrawLine(pen, Dpi.Px(11), Dpi.Px(19), Dpi.Px(22), Dpi.Px(19));
                    e.Graphics.DrawLine(pen, Dpi.Px(11), Dpi.Px(25), Dpi.Px(18), Dpi.Px(25));
                    // Play triangle on the right
                    Point[] pts = new Point[]
                    {
                        new Point(Dpi.Px(22), Dpi.Px(22)),
                        new Point(Dpi.Px(29), Dpi.Px(26)),
                        new Point(Dpi.Px(22), Dpi.Px(30))
                    };
                    e.Graphics.FillPolygon(tri, pts);
                }
            };
            Controls.Add(iconBox);

            lblTitle = new Label
            {
                Text = "Playlist Link Detected",
                Location = new Point(Dpi.Px(70), Dpi.Px(16)),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 11.5F, FontStyle.Bold),
                ForeColor = Theme.TextPrimary
            };
            Controls.Add(lblTitle);

            lblPlaylistInfo = new Label
            {
                Text = "Inspecting playlist details...",
                Location = new Point(Dpi.Px(70), Dpi.Px(40)),
                Size = new Size(Dpi.Px(580), Dpi.Px(20)),
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Theme.TextSecondary
            };
            Controls.Add(lblPlaylistInfo);

            int cardWidth = Dpi.Px(640);
            int cardLeft = Dpi.Px(20);

            // Card 1: Entire playlist
            cardEntire = new PlaylistChoiceCard
            {
                Location = new Point(cardLeft, Dpi.Px(70)),
                Size = new Size(cardWidth, Dpi.Px(62)),
                TitleText = "Download entire playlist",
                SubtitleText = "Queue all videos in this playlist as individual downloads",
                IsSelected = true
            };
            cardEntire.Click += (s, e) => SelectCard(PlaylistDownloadChoice.EntirePlaylist, false);
            Controls.Add(cardEntire);

            // Card 2: Single video only
            string singleSub = hasSingleVideo
                ? "Download only this video and skip the rest of the playlist"
                : "Download only the first video of the playlist";
            cardSingle = new PlaylistChoiceCard
            {
                Location = new Point(cardLeft, Dpi.Px(140)),
                Size = new Size(cardWidth, Dpi.Px(62)),
                TitleText = "Download single video only",
                SubtitleText = singleSub,
                IsSelected = false
            };
            cardSingle.Click += (s, e) => SelectCard(PlaylistDownloadChoice.SingleVideoOnly, false);
            Controls.Add(cardSingle);

            // Card 3: Custom range
            cardRange = new PlaylistChoiceCard
            {
                Location = new Point(cardLeft, Dpi.Px(210)),
                Size = new Size(cardWidth, Dpi.Px(72)),
                TitleText = "Custom range",
                SubtitleText = "Specify video range",
                IsSelected = false
            };
            cardRange.Click += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, true);

            // Range inputs panel inside Card 3 (matching mockup image layout)
            pnlRangeInputs = new Panel
            {
                Location = new Point(Dpi.Px(215), Dpi.Px(20)),
                Size = new Size(Dpi.Px(415), Dpi.Px(34)),
                BackColor = Color.Transparent
            };
            pnlRangeInputs.Click += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, true);

            lblFrom = new Label
            {
                Text = "From:",
                Location = new Point(0, Dpi.Px(6)),
                AutoSize = true,
                ForeColor = Theme.BrandPurpleLight,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular)
            };
            lblFrom.Click += (s, e) =>
            {
                SelectCard(PlaylistDownloadChoice.CustomRange, false);
                stepperFrom.FocusInput();
            };

            stepperFrom = new ModernNumericStepper
            {
                Location = new Point(Dpi.Px(46), Dpi.Px(1)),
                Size = new Size(Dpi.Px(68), Dpi.Px(32))
            };
            stepperFrom.Value = 1;
            stepperFrom.ValueChanged += (s, e) => UpdateRangeFeedback();
            stepperFrom.InputEntered += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, false);
            stepperFrom.EnterPressed += (s, e) =>
            {
                if (btnQueue.Enabled) btnQueue.PerformClick();
            };

            lblTo = new Label
            {
                Text = "To:",
                Location = new Point(Dpi.Px(126), Dpi.Px(6)),
                AutoSize = true,
                ForeColor = Theme.BrandPurpleLight,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular)
            };
            lblTo.Click += (s, e) =>
            {
                SelectCard(PlaylistDownloadChoice.CustomRange, false);
                stepperTo.FocusInput();
            };

            stepperTo = new ModernNumericStepper
            {
                Location = new Point(Dpi.Px(154), Dpi.Px(1)),
                Size = new Size(Dpi.Px(68), Dpi.Px(32))
            };
            stepperTo.Value = 15;
            stepperTo.ValueChanged += (s, e) => UpdateRangeFeedback();
            stepperTo.InputEntered += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, false);
            stepperTo.EnterPressed += (s, e) =>
            {
                if (btnQueue.Enabled) btnQueue.PerformClick();
            };

            lblRangeHint = new Label
            {
                Text = "",
                Location = new Point(Dpi.Px(236), Dpi.Px(6)),
                AutoSize = true,
                ForeColor = Color.FromArgb(215, 218, 230),
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular)
            };
            lblRangeHint.Click += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, false);

            lblRangeCount = new Label
            {
                Text = "",
                Location = new Point(Dpi.Px(280), Dpi.Px(6)),
                AutoSize = true,
                ForeColor = Theme.BrandPurpleLight,
                Font = new Font("Segoe UI", 9.8F, FontStyle.Regular)
            };
            lblRangeCount.Click += (s, e) => SelectCard(PlaylistDownloadChoice.CustomRange, false);

            pnlRangeInputs.Controls.Add(lblFrom);
            pnlRangeInputs.Controls.Add(stepperFrom);
            pnlRangeInputs.Controls.Add(lblTo);
            pnlRangeInputs.Controls.Add(stepperTo);
            pnlRangeInputs.Controls.Add(lblRangeHint);
            pnlRangeInputs.Controls.Add(lblRangeCount);
            cardRange.Controls.Add(pnlRangeInputs);

            Controls.Add(cardRange);

            // Divider
            Panel divider = new Panel
            {
                Location = new Point(Dpi.Px(20), Dpi.Px(300)),
                Size = new Size(cardWidth, Dpi.Px(1)),
                BackColor = Theme.BorderSubtle
            };
            Controls.Add(divider);

            // Status label
            lblStatus = new Label
            {
                Text = "\u25CF Checking playlist...",
                Location = new Point(Dpi.Px(20), Dpi.Px(324)),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.8F),
                ForeColor = Theme.AccentWarning
            };
            Controls.Add(lblStatus);

            // Buttons
            btnCancel = new ModernButton
            {
                Text = "Cancel",
                Location = new Point(Dpi.Px(355), Dpi.Px(316)),
                Size = new Size(Dpi.Px(85), Dpi.Px(38)),
                BaseColor = Theme.BgSurface,
                HoverColor = Theme.BgSurfaceHover,
                BorderColor = Theme.BorderSubtle,
                ForeColor = Theme.TextSecondary,
                Font = new Font("Segoe UI", 9F)
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCancel);

            btnQueue = new ModernButton
            {
                Text = "Queue Downloads",
                Location = new Point(Dpi.Px(450), Dpi.Px(316)),
                Size = new Size(Dpi.Px(210), Dpi.Px(38)),
                BaseColor = Theme.BrandPurple,
                HoverColor = Theme.BrandPurpleHover,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Enabled = hasSingleVideo
            };
            btnQueue.Click += BtnQueue_Click;
            Controls.Add(btnQueue);

            AcceptButton = btnQueue;
            CancelButton = btnCancel;

            Shown += (s, e) => LoadPlaylistInfoAsync();
        }

        private void SelectCard(PlaylistDownloadChoice choice, bool focusFrom = false)
        {
            selectedChoice = choice;
            cardEntire.IsSelected = (choice == PlaylistDownloadChoice.EntirePlaylist);
            cardSingle.IsSelected = (choice == PlaylistDownloadChoice.SingleVideoOnly);
            cardRange.IsSelected = (choice == PlaylistDownloadChoice.CustomRange);

            cardEntire.Invalidate();
            cardSingle.Invalidate();
            cardRange.Invalidate();

            UpdateQueueButtonText();

            if (choice == PlaylistDownloadChoice.CustomRange && focusFrom)
            {
                if (!stepperFrom.IsInputFocused && !stepperTo.IsInputFocused)
                {
                    stepperFrom.FocusInput();
                }
            }
        }

        private void UpdateRangeFeedback()
        {
            if (stepperFrom == null || stepperTo == null || lblRangeHint == null || lblRangeCount == null) return;

            int fromVal = stepperFrom.Value;
            int toVal = stepperTo.Value;
            int totalCount = (playlistInfo != null && playlistInfo.Entries.Count > 0)
                ? playlistInfo.Entries.Count
                : (stepperTo.MaxValue > 0 && stepperTo.MaxValue < 9999 ? stepperTo.MaxValue : 0);

            if (totalCount > 0)
            {
                lblRangeHint.Text = string.Format("of {0}", totalCount);
                lblRangeHint.Visible = true;
            }
            else
            {
                lblRangeHint.Text = "";
                lblRangeHint.Visible = false;
            }

            int hintRight = lblRangeHint.Visible ? lblRangeHint.Right : stepperTo.Right;
            lblRangeCount.Location = new Point(hintRight + Dpi.Px(14), Dpi.Px(6));

            if (fromVal < 1)
            {
                lblRangeCount.Text = "(Must start \u2265 1)";
                lblRangeCount.ForeColor = Theme.AccentWarning;
            }
            else if (fromVal > toVal)
            {
                lblRangeCount.Text = "(From > To)";
                lblRangeCount.ForeColor = Theme.AccentWarning;
            }
            else if (totalCount > 0 && toVal > totalCount)
            {
                lblRangeCount.Text = string.Format("(Max is {0})", totalCount);
                lblRangeCount.ForeColor = Theme.AccentWarning;
            }
            else
            {
                int count = toVal - fromVal + 1;
                lblRangeCount.Text = string.Format("({0} {1})", count, count == 1 ? "video" : "videos");
                lblRangeCount.ForeColor = Theme.BrandPurpleLight;
            }

            UpdateQueueButtonText();
        }

        private void UpdateQueueButtonText()
        {
            if (btnQueue == null) return;

            int totalCount = (playlistInfo != null && playlistInfo.Entries.Count > 0) ? playlistInfo.Entries.Count : 0;

            if (selectedChoice == PlaylistDownloadChoice.EntirePlaylist)
            {
                btnQueue.Text = totalCount > 0
                    ? string.Format("Queue Entire Playlist ({0})", totalCount)
                    : "Queue Entire Playlist";
                btnQueue.Enabled = totalCount > 0 || playlistInfo != null;
            }
            else if (selectedChoice == PlaylistDownloadChoice.SingleVideoOnly)
            {
                btnQueue.Text = "Queue Single Video";
                btnQueue.Enabled = true;
            }
            else // CustomRange
            {
                if (stepperFrom != null && stepperTo != null)
                {
                    int fromVal = stepperFrom.Value;
                    int toVal = stepperTo.Value;

                    if (fromVal <= toVal && fromVal >= 1)
                    {
                        int count = toVal - fromVal + 1;
                        btnQueue.Text = string.Format("Queue {0} {1}", count, count == 1 ? "Video" : "Videos");
                        btnQueue.Enabled = true;
                    }
                    else
                    {
                        btnQueue.Text = "Invalid Range";
                        btnQueue.Enabled = false;
                    }
                }
                else
                {
                    btnQueue.Text = "Queue Range";
                    btnQueue.Enabled = false;
                }
            }
        }

        private async void LoadPlaylistInfoAsync()
        {
            playlistInfo = await ExternalToolService.FetchPlaylistInfoAsync(ytDlpPath, playlistUrl);
            if (IsDisposed) return;

            if (playlistInfo != null && playlistInfo.Entries.Count > 0)
            {
                int count = playlistInfo.Count;
                string title = !string.IsNullOrEmpty(playlistInfo.Title) ? playlistInfo.Title : "Playlist";
                lblPlaylistInfo.Text = string.Format("{0}  \u2022  {1} videos", title, count);

                cardEntire.TitleText = string.Format("Download entire playlist ({0} videos)", count);
                cardEntire.SubtitleText = string.Format("Queue all {0} videos as individual downloads", count);
                cardEntire.BadgeText = string.Format("{0} videos", count);

                if (hasSingleVideo)
                {
                    string foundTitle = null;
                    foreach (PlaylistEntry e in playlistInfo.Entries)
                    {
                        if (e.Id == singleVideoId) { foundTitle = e.Title; break; }
                    }
                    if (!string.IsNullOrEmpty(foundTitle))
                    {
                        cardSingle.SubtitleText = string.Format("Current video: \"{0}\"", foundTitle);
                    }
                }
                else
                {
                    cardSingle.SubtitleText = string.Format("First video: \"{0}\"", playlistInfo.Entries[0].Title);
                }
                cardSingle.BadgeText = "1 video";

                stepperFrom.MinValue = 1;
                stepperFrom.MaxValue = count;
                stepperTo.MinValue = 1;
                stepperTo.MaxValue = count;

                stepperFrom.Value = 1;
                stepperTo.Value = Math.Min(15, count);

                lblStatus.Text = string.Format("\u25CF {0} videos ready", count);
                lblStatus.ForeColor = Theme.AccentSuccess;
                btnQueue.Enabled = true;
            }
            else
            {
                lblPlaylistInfo.Text = "Could not retrieve full video list.";
                lblStatus.Text = "\u25CF Playlist items ready";
                lblStatus.ForeColor = Theme.TextMuted;
                btnQueue.Enabled = true;
            }

            UpdateRangeFeedback();
            UpdateQueueButtonText();

            cardEntire.Invalidate();
            cardSingle.Invalidate();
            cardRange.Invalidate();
        }

        private void BtnQueue_Click(object sender, EventArgs e)
        {
            ResultUrls = new List<string>();
            ResultTitles = new List<string>();

            if (selectedChoice == PlaylistDownloadChoice.EntirePlaylist)
            {
                if (playlistInfo != null && playlistInfo.Entries.Count > 0)
                {
                    foreach (PlaylistEntry entry in playlistInfo.Entries)
                    {
                        ResultUrls.Add(entry.Url);
                        ResultTitles.Add(entry.Title);
                    }
                }
                else
                {
                    ResultUrls.Add(playlistUrl);
                    ResultTitles.Add(null);
                }
            }
            else if (selectedChoice == PlaylistDownloadChoice.SingleVideoOnly)
            {
                if (hasSingleVideo && !string.IsNullOrEmpty(singleVideoUrl))
                {
                    ResultUrls.Add(singleVideoUrl);
                    string title = null;
                    if (playlistInfo != null)
                    {
                        foreach (PlaylistEntry entry in playlistInfo.Entries)
                        {
                            if (entry.Id == singleVideoId)
                            {
                                title = entry.Title;
                                break;
                            }
                        }
                    }
                    ResultTitles.Add(title);
                }
                else if (playlistInfo != null && playlistInfo.Entries.Count > 0)
                {
                    ResultUrls.Add(playlistInfo.Entries[0].Url);
                    ResultTitles.Add(playlistInfo.Entries[0].Title);
                }
                else
                {
                    ResultUrls.Add(playlistUrl);
                    ResultTitles.Add(null);
                }
            }
            else // CustomRange
            {
                int fromVal = stepperFrom.Value;
                int toVal = stepperTo.Value;

                int max = (playlistInfo != null && playlistInfo.Entries.Count > 0) ? playlistInfo.Entries.Count : 9999;
                if (fromVal < 1) fromVal = 1;
                if (toVal < fromVal) toVal = fromVal;
                if (toVal > max) toVal = max;

                if (playlistInfo != null && playlistInfo.Entries.Count > 0)
                {
                    int startIdx = Math.Max(0, fromVal - 1);
                    int endIdx = Math.Min(playlistInfo.Entries.Count - 1, toVal - 1);
                    for (int i = startIdx; i <= endIdx; i++)
                    {
                        ResultUrls.Add(playlistInfo.Entries[i].Url);
                        ResultTitles.Add(playlistInfo.Entries[i].Title);
                    }
                }
                else
                {
                    ResultUrls.Add(playlistUrl);
                    ResultTitles.Add(null);
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // Modern Numeric Stepper (Matching User Mockup)
    internal class ModernNumericStepper : Panel
    {
        private readonly TextBox textBox;
        private bool isFocused;
        private bool hoverUp;
        private bool hoverDown;
        private int minValue = 1;
        private int maxValue = 9999;

        public event EventHandler ValueChanged;
        public event EventHandler InputEntered;
        public event EventHandler EnterPressed;

        public bool IsInputFocused { get { return textBox != null && textBox.Focused; } }

        public int Value
        {
            get
            {
                int val;
                if (int.TryParse(textBox.Text.Trim(), out val)) return val;
                return minValue;
            }
            set
            {
                int clamped = Math.Max(minValue, Math.Min(maxValue, value));
                textBox.Text = clamped.ToString();
            }
        }

        public int MinValue
        {
            get { return minValue; }
            set { minValue = value; }
        }

        public int MaxValue
        {
            get { return maxValue; }
            set { maxValue = value; }
        }

        public ModernNumericStepper()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(Dpi.Px(68), Dpi.Px(32));
            BackColor = Color.Transparent;

            textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(17, 16, 26),
                ForeColor = Theme.TextPrimary,
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                Text = "1"
            };

            PositionTextBox();
            Controls.Add(textBox);

            textBox.GotFocus += (s, e) =>
            {
                isFocused = true;
                Invalidate();
                if (InputEntered != null) InputEntered(this, EventArgs.Empty);
            };
            textBox.LostFocus += (s, e) =>
            {
                isFocused = false;
                Invalidate();
            };
            textBox.Click += (s, e) =>
            {
                if (textBox.SelectionLength == 0) textBox.SelectAll();
            };
            textBox.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                {
                    e.Handled = true;
                }
            };
            textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Up)
                {
                    Increment();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    Decrement();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    if (EnterPressed != null) EnterPressed(this, EventArgs.Empty);
                }
            };
            textBox.TextChanged += (s, e) =>
            {
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };

            Click += (s, e) => FocusInput();
        }

        public void FocusInput()
        {
            textBox.Focus();
            textBox.SelectAll();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            PositionTextBox();
        }

        private void PositionTextBox()
        {
            if (textBox == null) return;
            int btnWidth = Dpi.Px(23);
            int tbWidth = Width - btnWidth - Dpi.Px(4);
            int tbHeight = textBox.PreferredHeight;
            int tbY = (Height - tbHeight) / 2;
            textBox.Location = new Point(Dpi.Px(3), tbY);
            textBox.Size = new Size(tbWidth, tbHeight);
        }

        public void Increment()
        {
            int current = Value;
            if (current < maxValue)
            {
                Value = current + 1;
            }
        }

        public void Decrement()
        {
            int current = Value;
            if (current > minValue)
            {
                Value = current - 1;
            }
        }

        private Rectangle GetUpButtonRect()
        {
            int btnWidth = Dpi.Px(23);
            return new Rectangle(Width - btnWidth, 0, btnWidth, Height / 2);
        }

        private Rectangle GetDownButtonRect()
        {
            int btnWidth = Dpi.Px(23);
            int half = Height / 2;
            return new Rectangle(Width - btnWidth, half, btnWidth, Height - half);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool oldUp = hoverUp;
            bool oldDown = hoverDown;

            hoverUp = GetUpButtonRect().Contains(e.Location);
            hoverDown = GetDownButtonRect().Contains(e.Location);

            if (hoverUp != oldUp || hoverDown != oldDown)
            {
                Cursor = (hoverUp || hoverDown) ? Cursors.Hand : Cursors.IBeam;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverUp || hoverDown)
            {
                hoverUp = false;
                hoverDown = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (GetUpButtonRect().Contains(e.Location))
                {
                    Increment();
                    if (InputEntered != null) InputEntered(this, EventArgs.Empty);
                }
                else if (GetDownButtonRect().Contains(e.Location))
                {
                    Decrement();
                    if (InputEntered != null) InputEntered(this, EventArgs.Empty);
                }
                else
                {
                    FocusInput();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Clear background with parent card background for seamless rounded corners
            Color parentBg = Color.FromArgb(38, 32, 54);
            if (Parent != null && Parent.Parent is PlaylistChoiceCard)
            {
                PlaylistChoiceCard card = (PlaylistChoiceCard)Parent.Parent;
                parentBg = card.IsSelected
                    ? Color.FromArgb(38, 32, 54)
                    : (card.IsHovered ? Theme.BgSurfaceHover : Theme.BgSurface);
            }
            using (SolidBrush bgClear = new SolidBrush(parentBg))
            {
                g.FillRectangle(bgClear, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Dpi.Px(8);
            using (GraphicsPath path = Theme.CreateRoundedRectangle(rect, radius))
            {
                // Fill outer box
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(17, 16, 26)))
                {
                    g.FillPath(fill, path);
                }

                // Clip to rounded border so buttons and dividers stay inside nicely
                GraphicsState state = g.Save();
                g.SetClip(path);

                Rectangle upRect = GetUpButtonRect();
                Rectangle downRect = GetDownButtonRect();

                // Hover highlights on stepper arrows
                if (hoverUp)
                {
                    using (SolidBrush hoverBrush = new SolidBrush(Color.FromArgb(48, 42, 68)))
                    {
                        g.FillRectangle(hoverBrush, upRect);
                    }
                }
                if (hoverDown)
                {
                    using (SolidBrush hoverBrush = new SolidBrush(Color.FromArgb(48, 42, 68)))
                    {
                        g.FillRectangle(hoverBrush, downRect);
                    }
                }

                // Vertical divider line
                int splitX = upRect.Left;
                using (Pen divPen = new Pen(Color.FromArgb(42, 38, 58), 1f))
                {
                    g.DrawLine(divPen, splitX, 0, splitX, Height);
                    g.DrawLine(divPen, splitX, Height / 2, Width, Height / 2);
                }

                // Draw Up Chevron (˄)
                int upCx = upRect.Left + upRect.Width / 2;
                int upCy = upRect.Top + upRect.Height / 2;
                Color upColor = hoverUp ? Theme.TextPrimary : Color.FromArgb(188, 172, 240);
                using (Pen chevPen = new Pen(upColor, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    Point[] upPts = new Point[]
                    {
                        new Point(upCx - Dpi.Px(4), upCy + Dpi.Px(1)),
                        new Point(upCx, upCy - Dpi.Px(2)),
                        new Point(upCx + Dpi.Px(4), upCy + Dpi.Px(1))
                    };
                    g.DrawLines(chevPen, upPts);
                }

                // Draw Down Chevron (˅)
                int downCx = downRect.Left + downRect.Width / 2;
                int downCy = downRect.Top + downRect.Height / 2;
                Color downColor = hoverDown ? Theme.TextPrimary : Color.FromArgb(188, 172, 240);
                using (Pen chevPen = new Pen(downColor, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    Point[] downPts = new Point[]
                    {
                        new Point(downCx - Dpi.Px(4), downCy - Dpi.Px(2)),
                        new Point(downCx, downCy + Dpi.Px(1)),
                        new Point(downCx + Dpi.Px(4), downCy - Dpi.Px(2))
                    };
                    g.DrawLines(chevPen, downPts);
                }

                g.Restore(state);

                // Draw outer border (focused: purple, unfocused: dark subtle)
                Color borderCol = isFocused ? Theme.BorderFocus : Color.FromArgb(48, 44, 66);
                float penWidth = isFocused ? 1.8f : 1f;
                using (Pen pen = new Pen(borderCol, penWidth))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    internal class PlaylistChoiceCard : Panel
    {
        public bool IsSelected { get; set; }
        public string TitleText { get; set; }
        public string SubtitleText { get; set; }
        public string BadgeText { get; set; }
        public bool IsHovered { get { return isHovered; } }
        private bool isHovered;

        public PlaylistChoiceCard()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            TitleText = "";
            SubtitleText = "";
            BadgeText = "";
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = IsSelected
                ? Color.FromArgb(38, 32, 54)
                : (isHovered ? Theme.BgSurfaceHover : Theme.BgSurface);
            Color border = IsSelected
                ? Theme.BrandPurple
                : (isHovered ? Theme.BorderHover : Theme.BorderSubtle);

            using (GraphicsPath path = Theme.CreateRoundedRectangle(bounds, Dpi.Px(10)))
            {
                using (SolidBrush b = new SolidBrush(bg))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(border, IsSelected ? 1.8f : 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            // Radio circle
            int radioX = Dpi.Px(18);
            int radioY = Height / 2 - Dpi.Px(8);
            int radioSize = Dpi.Px(16);
            Rectangle rRadio = new Rectangle(radioX, radioY, radioSize, radioSize);

            using (Pen pRadio = new Pen(IsSelected ? Theme.BrandPurpleLight : Theme.TextMuted, 1.5f))
            {
                g.DrawEllipse(pRadio, rRadio);
            }

            if (IsSelected)
            {
                int innerSize = Dpi.Px(8);
                int innerX = radioX + (radioSize - innerSize) / 2;
                int innerY = radioY + (radioSize - innerSize) / 2;
                using (SolidBrush bInner = new SolidBrush(Theme.BrandPurple))
                {
                    g.FillEllipse(bInner, innerX, innerY, innerSize, innerSize);
                }
            }

            // Title
            int textX = Dpi.Px(46);
            int textY = string.IsNullOrEmpty(SubtitleText) ? (Height / 2 - Dpi.Px(10)) : Dpi.Px(13);
            using (Font fTitle = new Font("Segoe UI Semibold", 9.8F, FontStyle.Bold))
            {
                Color titleCol = IsSelected ? Theme.BrandPurpleLight : Theme.TextPrimary;
                TextRenderer.DrawText(g, TitleText, fTitle, new Point(textX, textY), titleCol, TextFormatFlags.NoPrefix);
            }

            // Subtitle
            if (!string.IsNullOrEmpty(SubtitleText))
            {
                using (Font fSub = new Font("Segoe UI", 8.2F, FontStyle.Regular))
                {
                    int subY = textY + Dpi.Px(21);
                    TextRenderer.DrawText(g, SubtitleText, fSub, new Point(textX, subY), Theme.TextMuted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }

            // Optional Badge on the right
            if (!string.IsNullOrEmpty(BadgeText))
            {
                using (Font fBadge = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold))
                {
                    Size bSize = TextRenderer.MeasureText(BadgeText, fBadge);
                    int bWidth = bSize.Width + Dpi.Px(14);
                    int bHeight = Dpi.Px(22);
                    int bX = Width - bWidth - Dpi.Px(18);
                    int bY = Height / 2 - bHeight / 2;

                    Rectangle bRect = new Rectangle(bX, bY, bWidth, bHeight);
                    using (GraphicsPath bPath = Theme.CreateRoundedRectangle(bRect, Dpi.Px(6)))
                    using (SolidBrush bBg = new SolidBrush(IsSelected ? Color.FromArgb(52, 42, 76) : Color.FromArgb(36, 40, 50)))
                    using (Pen bPen = new Pen(IsSelected ? Theme.BrandPurple : Theme.BorderSubtle, 1f))
                    {
                        g.FillPath(bBg, bPath);
                        g.DrawPath(bPen, bPath);
                    }

                    Color bTextCol = IsSelected ? Theme.BrandPurpleLight : Theme.TextSecondary;
                    TextRenderer.DrawText(g, BadgeText, fBadge, new Rectangle(bX, bY, bWidth, bHeight), bTextCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }
    }
}
