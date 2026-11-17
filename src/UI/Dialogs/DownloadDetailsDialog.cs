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
            ClientSize = new Size(Dpi.Px(560), Dpi.Px(320));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false; MaximizeBox = false;

            int inset = Dpi.Px(24);
            int contentWidth = Dpi.Px(560) - inset * 2;

            ModernButton details = Theme.CreateButton("Show technical details", false);
            ModernButton copy = Theme.CreateButton("Copy details", false);
            ModernButton close = Theme.CreateButton("Close", true, 80);
            close.DialogResult = DialogResult.OK;
            Panel footer = Theme.CreateDialogFooter(details, copy, close);

            Panel viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(inset, Dpi.Px(20), inset, Dpi.Px(16)) };
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };

            Label heading = Theme.CreateDialogTitle("Download couldn't finish");
            heading.Margin = new Padding(0, 0, 0, Dpi.Px(4));
            Label stage = Theme.CreateDialogText("While " + failure.Stage.ToLowerInvariant(), contentWidth);
            stage.ForeColor = Theme.TextMuted;
            stage.Margin = new Padding(0, 0, 0, Dpi.Px(14));
            Label message = Theme.CreateDialogText(failure.Message, contentWidth);
            message.ForeColor = Theme.DangerText;
            message.Font = Theme.GetFont("Segoe UI Semibold", 10F);
            message.Margin = new Padding(0, 0, 0, Dpi.Px(14));
            content.Controls.Add(heading);
            content.Controls.Add(stage);
            content.Controls.Add(message);

            CardPanel suggestion = new CardPanel { Width = contentWidth, Margin = new Padding(0, 0, 0, Dpi.Px(12)), CornerRadius = Dpi.Px(10) };
            Label caption = new Label
            {
                Text = "What you can do", AutoSize = true, Location = new Point(Dpi.Px(14), Dpi.Px(12)),
                Font = Theme.GetFont("Segoe UI Semibold", 9F), ForeColor = Theme.AccentText, BackColor = Theme.BgSurface
            };
            Label advice = Theme.CreateDialogText(failure.Suggestion, contentWidth - Dpi.Px(28));
            advice.BackColor = Theme.BgSurface;
            advice.Location = new Point(Dpi.Px(14), Dpi.Px(34));
            suggestion.Controls.Add(caption); suggestion.Controls.Add(advice);
            suggestion.Height = advice.Bottom + Dpi.Px(14);
            content.Controls.Add(suggestion);

            CardPanel technicalPanel = new CardPanel
            {
                Size = new Size(contentWidth, Dpi.Px(150)), Padding = new Padding(Dpi.Px(12)), Visible = false,
                Margin = Padding.Empty, CardFillColor = Theme.BgInput, CornerRadius = Dpi.Px(10)
            };
            Label technicalCaption = new Label { Text = "Original error message", Dock = DockStyle.Top, Height = Dpi.Px(24), ForeColor = Theme.TextMuted, BackColor = Theme.BgInput };
            RichTextBox technical = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false, Text = failure.TechnicalDetails,
                BackColor = Theme.BgInput, ForeColor = Theme.TextSecondary, Font = Theme.GetFont("Consolas", 9F), AccessibleName = "Original error message"
            };
            technicalPanel.Controls.Add(new DarkRichTextScrollHost(technical)); technicalPanel.Controls.Add(technicalCaption);
            content.Controls.Add(technicalPanel);
            viewport.Controls.Add(content);
            Controls.Add(viewport); Controls.Add(footer);

            bool expanded = false;
            Action fit = () =>
            {
                content.PerformLayout();
                Rectangle screen = Screen.FromControl(this).WorkingArea;
                int desired = content.PreferredSize.Height + Dpi.Px(36) + footer.Height;
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
                    Clipboard.SetText(failure.Message + "\r\n" + failure.Suggestion + "\r\nStage: " + failure.Stage + "\r\n\r\n" + failure.TechnicalDetails);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) feedback.Dispose();
            base.Dispose(disposing);
        }
    }
}
