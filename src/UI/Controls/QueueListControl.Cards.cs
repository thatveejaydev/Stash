using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MediaDownloader
{
    public partial class QueueListControl
    {
        private Rectangle GetFolderButtonRect(Rectangle rect)
        {
            return new Rectangle(rect.Right - (rect.Height == 64 ? 38 : 70), rect.Height == 64 ? rect.Y + 19 : rect.Bottom - 38, 26, 26);
        }

        private void DrawQueueCard(Graphics g, DownloadQueueItem item, Rectangle rect)
        {
            bool completed = item.Status == QueueItemStatus.Completed;
            using (GraphicsPath card = Theme.CreateRoundedRectangle(rect, completed ? 14 : 10))
            using (SolidBrush fill = new SolidBrush(item == hoveredItem ? Theme.BgSurfaceHover : completed ? Theme.BgTopBar : Theme.BgInput))
            using (Pen border = new Pen(item == SelectedItem ? Theme.BorderHover : Theme.BorderSubtle))
            {
                g.FillPath(fill, card);
                g.DrawPath(border, card);
            }

            // A media tile reserves the reference layout's artwork area without inventing a thumbnail.
            Rectangle tile = new Rectangle(rect.X + 12, rect.Y + 12, 40, 40);
            using (GraphicsPath tilePath = Theme.CreateRoundedRectangle(tile, 10))
            using (LinearGradientBrush tileFill = new LinearGradientBrush(tile, Color.FromArgb(52, 38, 78), Theme.BgSurface, 35f))
            using (Pen icon = new Pen(Theme.BrandPurpleLight, 1.5f))
            {
                g.FillPath(tileFill, tilePath);
                bool audio = item.Mode == DownloadMode.BestAudioOnlyMp3 || item.Mode == DownloadMode.BestAudioOnlyM4a || item.Mode == DownloadMode.BestAudioOnlyFlac;
                if (audio)
                {
                    g.DrawLine(icon, tile.X + 23, tile.Y + 12, tile.X + 23, tile.Y + 25);
                    g.DrawLine(icon, tile.X + 23, tile.Y + 12, tile.X + 29, tile.Y + 14);
                    g.DrawEllipse(icon, tile.X + 15, tile.Y + 22, 8, 5);
                }
                else
                {
                    g.DrawRectangle(icon, tile.X + 10, tile.Y + 14, 14, 12);
                    g.DrawPolygon(icon, new Point[] { new Point(tile.X + 24, tile.Y + 17), new Point(tile.X + 30, tile.Y + 14), new Point(tile.X + 30, tile.Y + 26), new Point(tile.X + 24, tile.Y + 23) });
                }
            }

            int textX = tile.Right + 12;
            int textWidth = Math.Max(1, rect.Right - textX - (completed ? 82 : 160));
            Color statusColor = Theme.AccentSuccess;
            if (item.Status == QueueItemStatus.Error || item.Status == QueueItemStatus.Cancelled) statusColor = Theme.AccentDanger;
            else if (item.Status == QueueItemStatus.Paused) statusColor = Theme.AccentWarning;
            else if (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.FetchingInfo) statusColor = Theme.TextSecondary;
            using (Font title = new Font("Segoe UI Semibold", 10F, FontStyle.Bold))
            using (Font detail = new Font("Segoe UI", 8.5F))
            {
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, item.Title, title, new Rectangle(textX, rect.Y + 10, textWidth, 23), Theme.TextPrimary, flags);
                string subtitle = completed ? YtDlpCommandBuilder.GetTargetExtension(item.Mode).TrimStart('.').ToUpperInvariant() + "   ·   Completed" : item.BadgeLabel;
                TextRenderer.DrawText(g, subtitle, detail, new Rectangle(textX, rect.Y + 32, textWidth, 20), Theme.TextSecondary, flags);
                if (completed)
                {
                    DrawCompletedActions(g, rect);
                    return;
                }
                TextRenderer.DrawText(g, item.StatusText, detail, new Rectangle(rect.Right - 150, rect.Y + 12, 136, 20), statusColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

                Rectangle track = new Rectangle(rect.X + 12, rect.Y + 63, rect.Width - 24, 4);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(track, 3))
                using (SolidBrush brush = new SolidBrush(Theme.BorderSubtle)) g.FillPath(brush, path);
                double percent = Math.Max(0, Math.Min(100, item.ProgressPercent));
                int fillWidth = (int)(track.Width * percent / 100);
                if (fillWidth > 0)
                {
                    Rectangle progress = new Rectangle(track.X, track.Y, fillWidth, 4);
                    using (GraphicsPath path = Theme.CreateRoundedRectangle(progress, 3))
                    using (SolidBrush brush = new SolidBrush(Theme.BrandPurple)) g.FillPath(brush, path);
                }
                string telemetry = string.Format("{0:0}%", percent);
                if (item.Status == QueueItemStatus.Downloading) telemetry += "   ·   " + item.Speed + "   ·   " + item.Eta + " remaining";
                TextRenderer.DrawText(g, telemetry, detail, new Rectangle(track.X, rect.Y + 78, Math.Max(1, track.Width - 110), 20), Theme.TextSecondary, flags);
            }

            using (Pen icon = new Pen(Theme.TextSecondary, 1.5f))
            {
                Rectangle folder = GetFolderButtonRect(rect);
                g.DrawLines(icon, new Point[] { new Point(folder.X + 6, folder.Y + 10), new Point(folder.X + 6, folder.Y + 7), new Point(folder.X + 12, folder.Y + 7), new Point(folder.X + 15, folder.Y + 10), new Point(folder.X + 21, folder.Y + 10), new Point(folder.X + 19, folder.Y + 19), new Point(folder.X + 5, folder.Y + 19), new Point(folder.X + 6, folder.Y + 10), new Point(folder.X + 21, folder.Y + 10) });
                if (CanPauseOrResume(item.Status))
                {
                    Rectangle pause = GetPauseResumeButtonRect(rect);
                    g.DrawEllipse(icon, pause.X + 6, pause.Y + 6, 14, 14);
                    if (item.Status == QueueItemStatus.Paused)
                        g.DrawPolygon(icon, new Point[] { new Point(pause.X + 11, pause.Y + 10), new Point(pause.X + 16, pause.Y + 13), new Point(pause.X + 11, pause.Y + 16) });
                    else { g.DrawLine(icon, pause.X + 11, pause.Y + 10, pause.X + 11, pause.Y + 16); g.DrawLine(icon, pause.X + 15, pause.Y + 10, pause.X + 15, pause.Y + 16); }
                }
                if (CanCancelStatus(item.Status))
                {
                    Rectangle cancel = GetActionButtonRect(rect);
                    g.DrawLine(icon, cancel.X + 9, cancel.Y + 9, cancel.X + 17, cancel.Y + 17);
                    g.DrawLine(icon, cancel.X + 17, cancel.Y + 9, cancel.X + 9, cancel.Y + 17);
                }
            }
        }

        private void DrawCompletedActions(Graphics g, Rectangle rect)
        {
            int cy = rect.Y + rect.Height / 2;
            int checkX = rect.Right - 57;
            using (Pen check = new Pen(Theme.AccentSuccess, 1.5f))
            using (Pen icon = new Pen(Theme.TextSecondary, 1.3f))
            {
                g.DrawEllipse(check, checkX - 6, cy - 6, 12, 12);
                g.DrawLines(check, new Point[] { new Point(checkX - 3, cy), new Point(checkX - 1, cy + 2), new Point(checkX + 3, cy - 3) });
                Rectangle folder = GetFolderButtonRect(rect);
                g.DrawLines(icon, new Point[] { new Point(folder.X + 6, cy + 5), new Point(folder.X + 6, cy - 4), new Point(folder.X + 12, cy - 4), new Point(folder.X + 15, cy - 1), new Point(folder.X + 21, cy - 1), new Point(folder.X + 19, cy + 5), new Point(folder.X + 6, cy + 5) });
            }
        }
    }
}
