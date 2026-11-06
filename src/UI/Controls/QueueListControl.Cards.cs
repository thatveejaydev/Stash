using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace MediaDownloader
{
    public partial class QueueListControl
    {
        private readonly Font cardTitleFont = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        private readonly Font cardDetailFont = new Font("Segoe UI", 8.5F);
        private static readonly int ProgressStripeSpacing = Dpi.Px(14);
        private readonly Dictionary<DownloadQueueItem, CardThumbnail> thumbnails = new Dictionary<DownloadQueueItem, CardThumbnail>();
        internal string ThumbnailDecoderPath { get; set; }

        private sealed class CardThumbnail
        {
            public Uri Uri;
            public Bitmap Image;
            public CancellationTokenSource Pending;

            public void Release()
            {
                if (Pending != null) Pending.Cancel();
                if (Image != null) Image.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                progressAnimationTimer.Dispose();
                foreach (CardThumbnail thumbnail in thumbnails.Values) thumbnail.Release();
                thumbnails.Clear();
                cardTitleFont.Dispose();
                cardDetailFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private async void RefreshThumbnail(DownloadQueueItem item)
        {
            if (item.ThumbnailUri == null || string.IsNullOrEmpty(ThumbnailDecoderPath)) return;
            CardThumbnail previous;
            if (thumbnails.TryGetValue(item, out previous) && previous.Uri == item.ThumbnailUri) return;
            if (previous != null) previous.Release();

            CardThumbnail thumbnail = new CardThumbnail { Uri = item.ThumbnailUri, Pending = new CancellationTokenSource() };
            thumbnails[item] = thumbnail;
            Bitmap image;
            using (CancellationTokenSource pending = thumbnail.Pending)
                image = await ThumbnailPreview.LoadAsync(thumbnail.Uri, ThumbnailDecoderPath, pending.Token);
            thumbnail.Pending = null;

            if (image == null) return;
            CardThumbnail current;
            if (!thumbnails.TryGetValue(item, out current) || current != thumbnail)
            {
                image.Dispose();
                return;
            }
            thumbnail.Image = image;
            Invalidate();
        }

        private void ReleaseThumbnail(DownloadQueueItem item)
        {
            CardThumbnail thumbnail;
            if (thumbnails.TryGetValue(item, out thumbnail))
            {
                thumbnails.Remove(item);
                thumbnail.Release();
            }
        }

        private static Rectangle GetProgressTrack(Rectangle row)
        {
            return new Rectangle(row.X + Dpi.Px(12), row.Y + Dpi.Px(63), row.Width - Dpi.Px(24), Dpi.Px(4));
        }

        private void AnimateProgressBars(object sender, EventArgs e)
        {
            bool needsAnimation = false;
            if (Visible && IsHandleCreated)
            {
                progressStripeOffset = (progressStripeOffset + Dpi.Px(1)) % ProgressStripeSpacing;
                int y = -scrollBar.Value + Dpi.Px(4);
                int width = GetCardWidth();
                foreach (DownloadQueueItem item in items)
                {
                    if (y >= ClientSize.Height) break;
                    Rectangle row = new Rectangle(Dpi.Px(2), y, width, GetRowHeight(item));
                    y += row.Height + RowGap;
                    if (item.Status != QueueItemStatus.Downloading) continue;

                    Rectangle track = GetProgressTrack(row);
                    if (track.Width <= 0 || !track.IntersectsWith(ClientRectangle)) continue;

                    double target = GetProgressPercent(item);
                    double previous;
                    displayedProgress.TryGetValue(item, out previous);
                    if (target <= 0 && previous <= 0) continue;

                    // Ease toward new reports. Snap back when yt-dlp starts a new stream.
                    double remaining = target - previous;
                    double next = remaining * track.Width <= 100 ? target : previous + remaining * 0.2;
                    displayedProgress[item] = next;
                    track.Width = (int)Math.Ceiling(track.Width * Math.Max(previous, next) / 100);
                    Invalidate(track);
                    needsAnimation = true;
                }
            }
            if (!needsAnimation) progressAnimationTimer.Stop();
        }

        private static double GetProgressPercent(DownloadQueueItem item)
        {
            return Math.Max(0, Math.Min(100, item.ProgressPercent));
        }

        private void DrawProgressStripes(Graphics g, Rectangle progress, GraphicsPath fillPath)
        {
            GraphicsState state = g.Save();
            g.SetClip(fillPath, CombineMode.Intersect);
            using (SolidBrush stripe = new SolidBrush(Color.FromArgb(30, Color.White)))
            {
                int stripeWidth = Dpi.Px(5);
                Point[] corners = new Point[4];
                for (int x = progress.Left + progressStripeOffset - ProgressStripeSpacing; x < progress.Right; x += ProgressStripeSpacing)
                {
                    corners[0] = new Point(x, progress.Top);
                    corners[1] = new Point(x + stripeWidth, progress.Top);
                    corners[2] = new Point(x + stripeWidth + progress.Height, progress.Bottom);
                    corners[3] = new Point(x + progress.Height, progress.Bottom);
                    g.FillPolygon(stripe, corners);
                }
            }
            g.Restore(state);
        }
        private Rectangle GetErrorActionRect(Rectangle row, int action)
        {
            return new Rectangle(row.Right - Dpi.Px(260) + action * Dpi.Px(84), row.Bottom - Dpi.Px(34), Dpi.Px(78), Dpi.Px(26));
        }
        private Rectangle GetFolderButtonRect(Rectangle rect)
        {
            return new Rectangle(rect.Right - (rect.Height == Dpi.Px(64) ? Dpi.Px(38) : Dpi.Px(70)), rect.Height == Dpi.Px(64) ? rect.Y + Dpi.Px(19) : rect.Bottom - Dpi.Px(38), Dpi.Px(26), Dpi.Px(26));
        }

        private void DrawMediaTile(Graphics g, DownloadQueueItem item, Rectangle tile)
        {
            CardThumbnail thumbnail;
            if (thumbnails.TryGetValue(item, out thumbnail) && thumbnail.Image != null)
            {
                using (GraphicsPath path = Theme.CreateRoundedRectangle(tile, 10))
                using (TextureBrush artwork = new TextureBrush(thumbnail.Image, WrapMode.TileFlipXY))
                using (Matrix placement = new Matrix(
                    (float)tile.Width / thumbnail.Image.Width, 0,
                    0, (float)tile.Height / thumbnail.Image.Height, tile.X, tile.Y))
                {
                    artwork.Transform = placement;
                    GraphicsState state = g.Save();
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.FillPath(artwork, path);
                    g.Restore(state);
                }
                return;
            }

            using (GraphicsPath tilePath = Theme.CreateRoundedRectangle(tile, 10))
            using (LinearGradientBrush tileFill = new LinearGradientBrush(tile, Color.FromArgb(52, 38, 78), Theme.BgSurface, 35f))
            using (Pen icon = new Pen(Theme.BrandPurpleLight, 1.5f))
            {
                g.FillPath(tileFill, tilePath);
                tile.X += (tile.Width - Dpi.Px(40)) / 2;
                bool audio = item.Mode == DownloadMode.BestAudioOnlyMp3 || item.Mode == DownloadMode.BestAudioOnlyM4a || item.Mode == DownloadMode.BestAudioOnlyFlac || item.Mode == DownloadMode.BestAudioOnlyWav;
                if (audio)
                {
                    g.DrawLine(icon, tile.X + Dpi.Px(23), tile.Y + Dpi.Px(12), tile.X + Dpi.Px(23), tile.Y + Dpi.Px(25));
                    g.DrawLine(icon, tile.X + Dpi.Px(23), tile.Y + Dpi.Px(12), tile.X + Dpi.Px(29), tile.Y + Dpi.Px(14));
                    g.DrawEllipse(icon, tile.X + Dpi.Px(15), tile.Y + Dpi.Px(22), Dpi.Px(8), Dpi.Px(5));
                }
                else
                {
                    g.DrawRectangle(icon, tile.X + Dpi.Px(10), tile.Y + Dpi.Px(14), Dpi.Px(14), Dpi.Px(12));
                    g.DrawPolygon(icon, new Point[] { new Point(tile.X + Dpi.Px(24), tile.Y + Dpi.Px(17)), new Point(tile.X + Dpi.Px(30), tile.Y + Dpi.Px(14)), new Point(tile.X + Dpi.Px(30), tile.Y + Dpi.Px(26)), new Point(tile.X + Dpi.Px(24), tile.Y + Dpi.Px(23)) });
                }
            }
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

            Rectangle tile = new Rectangle(rect.X + Dpi.Px(12), rect.Y + Dpi.Px(12), Dpi.Px(72), Dpi.Px(40));
            DrawMediaTile(g, item, tile);
            int textX = tile.Right + Dpi.Px(12);
            int textWidth = Math.Max(Dpi.Px(1), rect.Right - textX - (completed ? Dpi.Px(82) : Dpi.Px(160)));
            Color statusColor = Theme.AccentSuccess;
            if (item.Status == QueueItemStatus.Error || item.Status == QueueItemStatus.Cancelled) statusColor = Theme.AccentDanger;
            else if (item.Status == QueueItemStatus.Paused) statusColor = Theme.AccentWarning;
            else if (item.Status == QueueItemStatus.Queued || item.Status == QueueItemStatus.FetchingInfo) statusColor = Theme.TextSecondary;
            {
                Font title = cardTitleFont;
                Font detail = cardDetailFont;
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, item.Title, title, new Rectangle(textX, rect.Y + Dpi.Px(10), textWidth, Dpi.Px(23)), Theme.TextPrimary, flags);
                string subtitle = completed
                    ? (item.CompletedDetails == null ? YtDlpCommandBuilder.GetTargetExtension(item.Mode).TrimStart('.').ToUpperInvariant() + "   ·   Completed" : item.CompletedDetails.GetSummary(item.Mode))
                    : item.BadgeLabel;
                TextRenderer.DrawText(g, subtitle, detail, new Rectangle(textX, rect.Y + Dpi.Px(32), textWidth, Dpi.Px(20)), Theme.TextSecondary, flags);
                if (completed)
                {
                    DrawCompletedActions(g, rect);
                    return;
                }
                string statusLabel = item.StatusText;
                if (!string.IsNullOrWhiteSpace(statusLabel) && (item.Status == QueueItemStatus.FetchingInfo ||
                    item.Status == QueueItemStatus.Downloading || item.Status == QueueItemStatus.Merging))
                    statusLabel = statusLabel.TrimEnd().TrimEnd('.', '…') + "...";
                TextRenderer.DrawText(g, statusLabel, detail, new Rectangle(rect.Right - Dpi.Px(150), rect.Y + Dpi.Px(12), Dpi.Px(136), Dpi.Px(20)), statusColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

                if (item.Status == QueueItemStatus.Error)
                {
                    TextRenderer.DrawText(g, item.Failure == null ? "Something unexpected went wrong." : item.Failure.Message,
                        detail, new Rectangle(rect.X + Dpi.Px(12), rect.Y + Dpi.Px(60), rect.Width - Dpi.Px(24), Dpi.Px(20)), Theme.AccentDanger, flags);
                    string[] labels = { "Details", "Retry", "Remove" };
                    for (int action = 0; action < labels.Length; action++)
                    {
                        Rectangle button = GetErrorActionRect(rect, action);
                        using (SolidBrush brush = new SolidBrush(action == 1 ? Theme.BrandPurple : Theme.BgSurfaceHover)) g.FillRectangle(brush, button);
                        TextRenderer.DrawText(g, labels[action], detail, button, Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    return;
                }
                Rectangle track = GetProgressTrack(rect);
                using (GraphicsPath path = Theme.CreateRoundedRectangle(track, 3))
                using (SolidBrush brush = new SolidBrush(Theme.BorderSubtle)) g.FillPath(brush, path);
                double percent = GetProgressPercent(item);
                double fillPercent = percent;
                bool animate = item.Status == QueueItemStatus.Downloading && track.IntersectsWith(ClientRectangle);
                if (animate)
                {
                    displayedProgress.TryGetValue(item, out fillPercent);
                    fillPercent = Math.Min(fillPercent, percent);
                    displayedProgress[item] = fillPercent;
                    if (percent > 0) progressAnimationTimer.Start();
                }
                int fillWidth = (int)(track.Width * fillPercent / 100);
                if (fillWidth > 0)
                {
                    Rectangle progress = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                    using (GraphicsPath path = Theme.CreateRoundedRectangle(progress, 3))
                    using (SolidBrush brush = new SolidBrush(Theme.BrandPurple))
                    {
                        g.FillPath(brush, path);
                        if (animate) DrawProgressStripes(g, progress, path);
                    }
                }
                string telemetry = string.Format("{0:0}%", percent);
                bool waiting = item.Status == QueueItemStatus.FetchingInfo || item.Status == QueueItemStatus.Merging ||
                    item.StatusText == "Checking source" || item.StatusText == "Preparing files";
                if (waiting) telemetry = "Please wait…";
                if (item.Status == QueueItemStatus.Downloading && !waiting) telemetry += "   ·   " + item.Speed + "   ·   " + item.Eta + " remaining";
                TextRenderer.DrawText(g, telemetry, detail, new Rectangle(track.X, rect.Y + Dpi.Px(78), Math.Max(Dpi.Px(1), track.Width - Dpi.Px(item.Status == QueueItemStatus.Cancelled ? 190 : 110)), Dpi.Px(20)), Theme.TextSecondary, flags);
                if (item.Status == QueueItemStatus.Cancelled)
                {
                    for (int action = 1; action <= 2; action++)
                    {
                        Rectangle button = GetErrorActionRect(rect, action);
                        using (SolidBrush brush = new SolidBrush(action == 1 ? Theme.BrandPurple : Theme.BgSurfaceHover)) g.FillRectangle(brush, button);
                        TextRenderer.DrawText(g, action == 1 ? "Retry" : "Remove", detail, button, Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    return;
                }
            }

            using (Pen icon = new Pen(Theme.TextSecondary, 1.5f))
            {
                Rectangle folder = GetFolderButtonRect(rect);
                g.DrawLines(icon, new Point[] { new Point(folder.X + Dpi.Px(6), folder.Y + Dpi.Px(10)), new Point(folder.X + Dpi.Px(6), folder.Y + Dpi.Px(7)), new Point(folder.X + Dpi.Px(12), folder.Y + Dpi.Px(7)), new Point(folder.X + Dpi.Px(15), folder.Y + Dpi.Px(10)), new Point(folder.X + Dpi.Px(21), folder.Y + Dpi.Px(10)), new Point(folder.X + Dpi.Px(19), folder.Y + Dpi.Px(19)), new Point(folder.X + Dpi.Px(5), folder.Y + Dpi.Px(19)), new Point(folder.X + Dpi.Px(6), folder.Y + Dpi.Px(10)), new Point(folder.X + Dpi.Px(21), folder.Y + Dpi.Px(10)) });
                if (CanPauseOrResume(item.Status))
                {
                    Rectangle pause = GetPauseResumeButtonRect(rect);
                    g.DrawEllipse(icon, pause.X + Dpi.Px(6), pause.Y + Dpi.Px(6), Dpi.Px(14), Dpi.Px(14));
                    if (item.Status == QueueItemStatus.Paused)
                        g.DrawPolygon(icon, new Point[] { new Point(pause.X + Dpi.Px(11), pause.Y + Dpi.Px(10)), new Point(pause.X + Dpi.Px(16), pause.Y + Dpi.Px(13)), new Point(pause.X + Dpi.Px(11), pause.Y + Dpi.Px(16)) });
                    else { g.DrawLine(icon, pause.X + Dpi.Px(11), pause.Y + Dpi.Px(10), pause.X + Dpi.Px(11), pause.Y + Dpi.Px(16)); g.DrawLine(icon, pause.X + Dpi.Px(15), pause.Y + Dpi.Px(10), pause.X + Dpi.Px(15), pause.Y + Dpi.Px(16)); }
                }
                if (CanCancelStatus(item.Status))
                {
                    Rectangle cancel = GetActionButtonRect(rect);
                    g.DrawLine(icon, cancel.X + Dpi.Px(9), cancel.Y + Dpi.Px(9), cancel.X + Dpi.Px(17), cancel.Y + Dpi.Px(17));
                    g.DrawLine(icon, cancel.X + Dpi.Px(17), cancel.Y + Dpi.Px(9), cancel.X + Dpi.Px(9), cancel.Y + Dpi.Px(17));
                }
            }
        }

        private void DrawCompletedActions(Graphics g, Rectangle rect)
        {
            int cy = rect.Y + rect.Height / 2;
            int checkX = rect.Right - Dpi.Px(57);
            using (Pen check = new Pen(Theme.AccentSuccess, 1.5f))
            using (Pen icon = new Pen(Theme.TextSecondary, 1.3f))
            {
                g.DrawEllipse(check, checkX - Dpi.Px(6), cy - Dpi.Px(6), Dpi.Px(12), Dpi.Px(12));
                g.DrawLines(check, new Point[] { new Point(checkX - Dpi.Px(3), cy), new Point(checkX - Dpi.Px(1), cy + Dpi.Px(2)), new Point(checkX + Dpi.Px(3), cy - Dpi.Px(3)) });
                Rectangle folder = GetFolderButtonRect(rect);
                g.DrawLines(icon, new Point[] { new Point(folder.X + Dpi.Px(6), cy + Dpi.Px(5)), new Point(folder.X + Dpi.Px(6), cy - Dpi.Px(4)), new Point(folder.X + Dpi.Px(12), cy - Dpi.Px(4)), new Point(folder.X + Dpi.Px(15), cy - Dpi.Px(1)), new Point(folder.X + Dpi.Px(21), cy - Dpi.Px(1)), new Point(folder.X + Dpi.Px(19), cy + Dpi.Px(5)), new Point(folder.X + Dpi.Px(6), cy + Dpi.Px(5)) });
            }
        }
    }
}
