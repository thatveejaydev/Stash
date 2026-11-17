using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Stash
{
    internal sealed class DownloadDetailsDialog : ThemedForm
    {
        private readonly Timer feedback = new Timer { Interval = 2500 };

        public DownloadDetailsDialog(DownloadFailure failure)
        {
            Text = "Download details";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.GetFont("Segoe UI", 9.5F);
            ClientSize = new Size(Dpi.Px(620), Dpi.Px(340));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false; MaximizeBox = false;

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = Dpi.Px(58), BackColor = Theme.BgTopBar };
            ModernButton details = CreateButton("Show technical details", 182, false);
            details.Location = new Point(Dpi.Px(18), Dpi.Px(12));
            ModernButton copy = CreateButton("Copy details", 112, false);
            copy.Location = new Point(Dpi.Px(382), Dpi.Px(12));
            ModernButton close = CreateButton("Close", 96, true);
            close.Location = new Point(Dpi.Px(506), Dpi.Px(12));
            close.DialogResult = DialogResult.OK;
            footer.Controls.Add(details); footer.Controls.Add(copy); footer.Controls.Add(close);

            Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Dpi.Px(18)) };
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            Label heading = CreateLabel("Download couldn't finish", 14, Theme.TextPrimary, 5);
            heading.Font = Theme.GetFont("Segoe UI Semibold", 14F, FontStyle.Bold);
            content.Controls.Add(heading);
            content.Controls.Add(CreateLabel("Stage: " + failure.Stage, 9F, Theme.TextMuted, 12));
            content.Controls.Add(CreateLabel(failure.Message, 10, Theme.TextPrimary, 12));

            CardPanel suggestion = new CardPanel { Size = new Size(Dpi.Px(580), Dpi.Px(80)), Margin = new Padding(0, 0, 0, Dpi.Px(12)) };
            Label caption = CreateLabel("WHAT YOU CAN DO", 8, Theme.BrandPurpleLight, 8);
            caption.Location = new Point(Dpi.Px(12), Dpi.Px(10));
            Label advice = CreateLabel(failure.Suggestion, 9.5F, Theme.TextSecondary, 0);
            advice.MaximumSize = new Size(Dpi.Px(552), 0);
            advice.Location = new Point(Dpi.Px(12), Dpi.Px(30));
            suggestion.Controls.Add(caption); suggestion.Controls.Add(advice);
            suggestion.Height = advice.Bottom + Dpi.Px(12);
            content.Controls.Add(suggestion);

            CardPanel technicalPanel = new CardPanel { Size = new Size(Dpi.Px(580), Dpi.Px(140)), Padding = new Padding(Dpi.Px(12)), Visible = false, Margin = Padding.Empty, CardFillColor = Theme.BgInput };
            Label technicalCaption = new Label { Text = "Original error message", Dock = DockStyle.Top, Height = Dpi.Px(24), ForeColor = Theme.TextMuted };
            RichTextBox technical = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, Text = failure.TechnicalDetails, BackColor = Theme.BgInput, ForeColor = Theme.TextSecondary, Font = Theme.GetFont("Consolas", 9F), AccessibleName = "Original error message" };
            technicalPanel.Controls.Add(new DarkRichTextScrollHost(technical)); technicalPanel.Controls.Add(technicalCaption);
            content.Controls.Add(technicalPanel);
            viewport.Controls.Add(content);
            Controls.Add(viewport); Controls.Add(footer);
            bool expanded = false;
            Action fit = () =>
            {
                content.PerformLayout();
                Rectangle screen = Screen.FromControl(this).WorkingArea;
                int desired = content.PreferredSize.Height + Dpi.Px(94);
                ClientSize = new Size(ClientSize.Width, Math.Min(desired, Math.Max(Dpi.Px(200), screen.Height - Dpi.Px(80))));
                Top = Math.Max(screen.Top, Math.Min(Top, screen.Bottom - Height));
            };
            details.Click += (s, e) =>
            {
                expanded = !expanded;
                technicalPanel.Visible = expanded;
                details.Text = expanded ? "Hide technical details" : "Show technical details";
                fit();
            };
            copy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText("Stage: " + failure.Stage + "\r\n" + failure.Message + "\r\n" + failure.Suggestion + "\r\n\r\n" + failure.TechnicalDetails);
                    copy.Text = "Copied";
                }
                catch (ExternalException) { copy.Text = "Try again"; }
                feedback.Stop(); feedback.Start();
            };
            feedback.Tick += (s, e) => { feedback.Stop(); copy.Text = "Copy details"; };
            Load += (s, e) => fit();
            Shown += (s, e) => close.Focus();
            AcceptButton = close; CancelButton = close;
        }

        private static Label CreateLabel(string text, float size, Color color, int bottomMargin)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(Dpi.Px(550), 0), Font = Theme.GetFont("Segoe UI", size), ForeColor = color, Margin = new Padding(0, 0, 0, Dpi.Px(bottomMargin)), UseMnemonic = false };
        }

        private static ModernButton CreateButton(string text, int width, bool primary)
        {
            return new ModernButton { Text = text, Size = new Size(Dpi.Px(width), Dpi.Px(34)), Font = Theme.GetFont("Segoe UI Semibold", 9F), CornerRadius = Dpi.Px(8), BaseColor = primary ? Theme.BrandPurple : Theme.BgTopBar, HoverColor = primary ? Theme.BrandPurpleHover : Theme.BgSurfaceHover, BorderColor = primary ? Color.Empty : Theme.BorderSubtle };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) feedback.Dispose();
            base.Dispose(disposing);
        }
    }
}
