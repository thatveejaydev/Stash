using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Stash
{
    public partial class QueueListControl
    {
        private static Font SectionFont { get { return Theme.GetFont("Segoe UI Semibold", 9F); } }
        private static Font TitleFont { get { return Theme.GetFont("Segoe UI Semibold", 10F); } }
        private static Font DetailFont { get { return Theme.GetFont("Segoe UI", 8.75F); } }
        private static readonly int ProgressStripeSpacing = Dpi.Px(14);
        private static readonly Size TileSize = new Size(Dpi.Px(64), Dpi.Px(36));
        private const TextFormatFlags LineFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

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

        private static double GetProgressPercent(DownloadQueueItem item)
        {
            return Math.Max(0, Math.Min(100, item.ProgressPercent));
        }

        // ---- Card geometry ----

        private static Rectangle GetTile(Rectangle card)
        {
            return new Rectangle(card.X + Dpi.Px(12), card.Y + (card.Height - TileSize.Height) / 2, TileSize.Width, TileSize.Height);
        }

        private int GetTextRight(DownloadQueueItem item, Rectangle card)
        {
            int right = card.Right - Dpi.Px(14);
            foreach (KeyValuePair<CardAction, Rectangle> action in GetCardActions(item, card))
                right = Math.Min(right, action.Value.Left - Dpi.Px(10));
            return right;
        }

        private Rectangle GetProgressTrack(DownloadQueueItem item, Rectangle card)
        {
            int x = GetTile(card).Right + Dpi.Px(14);
            return new Rectangle(x, card.Y + Dpi.Px(41), Math.Max(Dpi.Px(1), GetTextRight(item, card) - x), Dpi.Px(4));
        }

        // ---- Cards ----

        private void DrawActiveCard(Graphics g, DownloadQueueItem item, Rectangle rect)
        {
            Rectangle shape = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            bool selected = item == SelectedItem && Focused;
            using (GraphicsPath card = Theme.CreateRoundedRectangle(shape, Dpi.Px(10)))
            using (SolidBrush fill = new SolidBrush(item == hoveredItem ? Theme.BgSurface : Theme.BgTopBar))
            using (Pen border = new Pen(selected ? Theme.BorderFocus : item == SelectedItem ? Theme.BorderHover : Theme.BorderSubtle))
            {
                g.FillPath(fill, card);
                g.DrawPath(border, card);
            }

            Rectangle tile = GetTile(rect);
            DrawMediaTile(g, item, tile);
            int textX = tile.Right + Dpi.Px(14);
            int textWidth = Math.Max(Dpi.Px(1), GetTextRight(item, rect) - textX);
            TextRenderer.DrawText(g, item.Title, TitleFont, new Rectangle(textX, rect.Y + Dpi.Px(11), textWidth, Dpi.Px(22)), Theme.TextPrimary, LineFlags);

            if (item.Status == QueueItemStatus.Error || item.Status == QueueItemStatus.Cancelled)
            {
                string message = item.Status == QueueItemStatus.Error
                    ? (item.Failure == null ? "Something unexpected went wrong." : item.Failure.Message)
                    : "Cancelled";
                TextRenderer.DrawText(g, message, DetailFont, new Rectangle(textX, rect.Y + Dpi.Px(34), textWidth, Dpi.Px(18)),
                    item.Status == QueueItemStatus.Error ? Theme.DangerText : Theme.TextSecondary, LineFlags);
                TextRenderer.DrawText(g, item.Status == QueueItemStatus.Error ? "Retry, or open the details for more information." : "Retry to start again.",
                    DetailFont, new Rectangle(textX, rect.Y + Dpi.Px(52), textWidth, Dpi.Px(18)), Theme.TextMuted, LineFlags);
            }
            else
            {
                DrawProgress(g, item, GetProgressTrack(item, rect));
                TextRenderer.DrawText(g, GetTelemetry(item), DetailFont, new Rectangle(textX, rect.Y + Dpi.Px(50), textWidth, Dpi.Px(18)),
                    item.Status == QueueItemStatus.Paused ? Theme.AccentWarning : Theme.TextSecondary, LineFlags);
            }
            DrawActions(g, item, rect);
        }

        private void DrawCompletedCard(Graphics g, DownloadQueueItem item, Rectangle rect)
        {
            bool hovered = item == hoveredItem;
            bool selected = item == SelectedItem;
            if (hovered || selected)
            {
                Rectangle shape = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                using (GraphicsPath card = Theme.CreateRoundedRectangle(shape, Dpi.Px(10)))
                using (SolidBrush fill = new SolidBrush(hovered ? Theme.BgSurface : Theme.BgTopBar))
                {
                    g.FillPath(fill, card);
                    if (selected && Focused)
                        using (Pen border = new Pen(Theme.BorderFocus)) g.DrawPath(border, card);
                }
            }
            Rectangle tile = GetTile(rect);
            DrawMediaTile(g, item, tile);
            int textX = tile.Right + Dpi.Px(14);
            int textWidth = Math.Max(Dpi.Px(1), GetTextRight(item, rect) - textX);
            string details = item.CompletedDetails == null
                ? YtDlpCommandBuilder.GetTargetExtension(item.Mode).TrimStart('.').ToUpperInvariant()
                : item.CompletedDetails.GetSummary(item.Mode).Replace("   ·   ", "  ·  ");
            if (item.Mode == DownloadMode.TranscriptOnly) details = item.BadgeLabel;
            if (details.EndsWith("  ·  Completed")) details = details.Substring(0, details.Length - "  ·  Completed".Length);
            TextRenderer.DrawText(g, item.Title, TitleFont, new Rectangle(textX, rect.Y + Dpi.Px(9), textWidth, Dpi.Px(22)), Theme.TextPrimary, LineFlags);
            TextRenderer.DrawText(g, details, DetailFont, new Rectangle(textX, rect.Y + Dpi.Px(31), textWidth, Dpi.Px(18)), Theme.TextSecondary, LineFlags);
            DrawActions(g, item, rect);
        }

        private static string GetTelemetry(DownloadQueueItem item)
        {
            double percent = GetProgressPercent(item);
            string status = (item.StatusText ?? "").Trim().TrimEnd('.', '…');
            if (item.Mode == DownloadMode.TranscriptOnly && item.Status == QueueItemStatus.Downloading && status == "Downloading captions")
                return status + "…";
            switch (item.Status)
            {
                case QueueItemStatus.Queued:
                    return string.IsNullOrEmpty(status) || status == "Queued" ? "Waiting to start" : status + "…";
                case QueueItemStatus.Paused:
                    return percent > 0 ? string.Format("Paused  ·  {0:0}%", percent) : "Paused";
                case QueueItemStatus.FetchingInfo:
                case QueueItemStatus.Merging:
                    return status + "…";
                default:
                    if (status == "Checking source" || status == "Preparing files" || status == "Pausing" || status == "Cancelling")
                        return status + "…";
                    string line = string.Format("{0:0}%", percent);
                    if (!string.IsNullOrEmpty(item.Speed) && item.Speed != "—") line += "  ·  " + item.Speed;
                    if (!string.IsNullOrEmpty(item.Eta) && item.Eta != "—" && item.Eta != "NA" && item.Eta != "Unknown") line += "  ·  " + item.Eta + " left";
                    return line;
            }
        }

        private void DrawProgress(Graphics g, DownloadQueueItem item, Rectangle track)
        {
            using (GraphicsPath path = Theme.CreateRoundedRectangle(track, Dpi.Px(2)))
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
            if (fillWidth <= 0) return;
            Rectangle progress = new Rectangle(track.X, track.Y, fillWidth, track.Height);
            Color color = item.Status == QueueItemStatus.Paused ? Theme.AccentWarning : Theme.BrandPurple;
            using (GraphicsPath path = Theme.CreateRoundedRectangle(progress, Dpi.Px(2)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
                if (animate) DrawProgressStripes(g, progress, path);
            }
        }

        private void DrawActions(Graphics g, DownloadQueueItem item, Rectangle rect)
        {
            foreach (KeyValuePair<CardAction, Rectangle> action in GetCardActions(item, rect))
            {
                bool hovered = item == hoveredItem && hoveredAction == action.Key;
                if (hovered)
                    using (GraphicsPath path = Theme.CreateRoundedRectangle(action.Value, Dpi.Px(8)))
                    using (SolidBrush fill = new SolidBrush(Theme.BgSurfaceHover)) g.FillPath(fill, path);
                IconKind icon;
                switch (action.Key)
                {
                    case CardAction.PauseResume: icon = item.Status == QueueItemStatus.Paused ? IconKind.Play : IconKind.Pause; break;
                    case CardAction.Retry: icon = IconKind.Retry; break;
                    case CardAction.Details: icon = IconKind.Info; break;
                    case CardAction.OpenFile: icon = IconKind.Play; break;
                    case CardAction.OpenFolder: icon = IconKind.Folder; break;
                    case CardAction.More: icon = IconKind.More; break;
                    default: icon = IconKind.Close; break;
                }
                Color color = hovered ? Theme.TextPrimary : Theme.TextSecondary;
                if (hovered && (action.Key == CardAction.Cancel || action.Key == CardAction.Remove)) color = Theme.DangerText;
                Icons.Draw(g, icon, action.Value, color);
            }
        }

        private void DrawMediaTile(Graphics g, DownloadQueueItem item, Rectangle tile)
        {
            CardThumbnail thumbnail;
            bool transcript = item.Mode == DownloadMode.TranscriptOnly;
            using (GraphicsPath path = Theme.CreateRoundedRectangle(tile, Dpi.Px(6)))
            {
                if (!transcript && thumbnails.TryGetValue(item, out thumbnail) && thumbnail.Image != null)
                {
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
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(44, 36, 66))) g.FillPath(fill, path);
            }
            bool audio = item.Mode == DownloadMode.BestAudioOnlyMp3 || item.Mode == DownloadMode.BestAudioOnlyM4a ||
                item.Mode == DownloadMode.BestAudioOnlyFlac || item.Mode == DownloadMode.BestAudioOnlyWav;
            IconKind icon = transcript ? IconKind.Transcript : audio ? IconKind.Audio : IconKind.Video;
            Icons.Draw(g, icon, tile, Theme.BrandPurpleLight);
        }

        // ---- Progress animation ----

        private void AnimateProgressBars(object sender, EventArgs e)
        {
            bool needsAnimation = false;
            if (Visible && IsHandleCreated)
            {
                progressStripeOffset = (progressStripeOffset + Dpi.Px(1)) % ProgressStripeSpacing;
                foreach (Entry entry in BuildLayout())
                {
                    if (entry.Item == null || entry.Item.Status != QueueItemStatus.Downloading) continue;
                    Rectangle rect = ToClient(entry);
                    if (rect.Top >= ClientSize.Height) break;
                    Rectangle track = GetProgressTrack(entry.Item, rect);
                    if (track.Width <= 0 || !track.IntersectsWith(ClientRectangle)) continue;

                    double target = GetProgressPercent(entry.Item);
                    double previous;
                    displayedProgress.TryGetValue(entry.Item, out previous);
                    if (target <= 0 && previous <= 0) continue;

                    // Ease toward new reports. Snap back when yt-dlp starts a new stream.
                    double remaining = target - previous;
                    double next = remaining * track.Width <= 100 ? target : previous + remaining * 0.2;
                    displayedProgress[entry.Item] = next;
                    track.Width = (int)Math.Ceiling(track.Width * Math.Max(previous, next) / 100);
                    Invalidate(track);
                    needsAnimation = true;
                }
            }
            if (!needsAnimation) progressAnimationTimer.Stop();
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
    }
}
