using System;
using System.IO;
using System.Text;

namespace MediaDownloader
{
    public static class YtDlpCommandBuilder
    {
        public static string GetTargetExtension(DownloadMode mode)
        {
            switch (mode)
            {
                case DownloadMode.BestVideoAndAudioMp4: return ".mp4";
                case DownloadMode.BestVideoAndAudioMkv: return ".mkv";
                case DownloadMode.BestAudioOnlyMp3: return ".mp3";
                case DownloadMode.BestAudioOnlyM4a: return ".m4a";
                case DownloadMode.BestAudioOnlyFlac: return ".flac";
                default: return ".mp4";
            }
        }

        public static int GetResolutionHeightCap(VideoResolution res)
        {
            switch (res)
            {
                case VideoResolution.Uhd2160p: return 2160;
                case VideoResolution.Qhd1440p: return 1440;
                case VideoResolution.Fhd1080p: return 1080;
                case VideoResolution.Hd720p: return 720;
                case VideoResolution.Sd480p: return 480;
                case VideoResolution.Low360p: return 360;
                default: return 0; // Best / Uncapped
            }
        }

        public static string BuildBadgeLabel(DownloadMode mode, VideoResolution res, bool meta, bool thumb, int concurrentFragments)
        {
            string formatPart;
            bool isVideo = (mode == DownloadMode.BestVideoAndAudioMp4 || mode == DownloadMode.BestVideoAndAudioMkv);

            if (isVideo)
            {
                string container = (mode == DownloadMode.BestVideoAndAudioMp4) ? "MP4" : "MKV";
                string resText = "BEST";
                switch (res)
                {
                    case VideoResolution.Uhd2160p: resText = "4K"; break;
                    case VideoResolution.Qhd1440p: resText = "1440P"; break;
                    case VideoResolution.Fhd1080p: resText = "1080P"; break;
                    case VideoResolution.Hd720p: resText = "720P"; break;
                    case VideoResolution.Sd480p: resText = "480P"; break;
                    case VideoResolution.Low360p: resText = "360P"; break;
                }
                formatPart = container + " • " + resText;
            }
            else
            {
                switch (mode)
                {
                    case DownloadMode.BestAudioOnlyMp3: formatPart = "MP3 • 320K"; break;
                    case DownloadMode.BestAudioOnlyM4a: formatPart = "M4A • BEST"; break;
                    case DownloadMode.BestAudioOnlyFlac: formatPart = "FLAC • LOSSLESS"; break;
                    default: formatPart = "AUDIO"; break;
                }
            }

            if (meta || thumb)
            {
                formatPart += " • TAGS";
            }

            return formatPart.Replace(" • ", "   ·   ");
        }

        public static string BuildArguments(DownloadQueueItem item, string ffmpegPath)
        {
            string safeCustomName = !string.IsNullOrEmpty(item.CustomBaseFileName)
                ? StorageFileService.SanitizeFileName(item.CustomBaseFileName)
                : null;

            string outputTemplate = !string.IsNullOrEmpty(safeCustomName)
                ? safeCustomName.Replace("%", "%%") + ".%(ext)s"
                : "%(title).100B.%(ext)s";

            StringBuilder args = new StringBuilder();
            args.AppendFormat("--paths \"home:{0}\" --paths \"temp:{1}\" --print \"after_move:STASH_FINAL:%(filepath)s\" --no-simulate ", Path.GetFullPath(item.OutputFolder), item.TemporaryFolder);
            args.Append("--newline --no-colors --progress --no-playlist --continue --windows-filenames --trim-filenames 120 ");

            // Multi-Connection Turbo Downloading: concurrent DASH/HLS fragments + chunked HTTP Range requests to bypass single-stream server throttling
            if (item.ConcurrentFragments > 1)
            {
                args.AppendFormat("--concurrent-fragments {0} --buffer-size 16K --http-chunk-size 10M ", item.ConcurrentFragments);
            }

            if (!string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath))
            {
                string ffmpegDir = Path.GetDirectoryName(ffmpegPath);
                args.AppendFormat("--ffmpeg-location \"{0}\" ", ffmpegDir);
            }

            int heightCap = GetResolutionHeightCap(item.Resolution);

            switch (item.Mode)
            {
                case DownloadMode.BestVideoAndAudioMp4:
                {
                    // Adaptive Smart Muxing Without Re-encoding:
                    // 1) For <= 1080p caps: prioritize native H.264 (avc) + AAC (mp4a/m4a) for instant 1-second FFmpeg stream copy and universal compatibility, with safe fallbacks.
                    // 2) For Best / 1440p / 4K: sort by highest resolution first, then prefer H.264/AAC at that resolution, and always prefer native AAC audio (m4a) so FFmpeg never has to re-encode audio.
                    string mp4Filter;
                    string mp4Sort;
                    if (heightCap > 0 && heightCap <= 1080)
                    {
                        mp4Filter = string.Format(
                            "bv*[vcodec^=avc][height<={0}]+ba[acodec^=mp4a]/bv*[ext=mp4][height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b",
                            heightCap);
                        mp4Sort = string.Format("res:{0},vcodec:h264,acodec:aac,ext:mp4:m4a", heightCap);
                    }
                    else if (heightCap > 1080)
                    {
                        mp4Filter = string.Format(
                            "bv*[height<={0}]+ba[acodec^=mp4a]/bv*[height<={0}]+ba[ext=m4a]/bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b",
                            heightCap);
                        mp4Sort = string.Format("res:{0},vcodec:h264,acodec:aac,ext:mp4:m4a", heightCap);
                    }
                    else
                    {
                        mp4Filter = "bv*+ba[acodec^=mp4a]/bv*+ba[ext=m4a]/bv*+ba/b";
                        mp4Sort = "res,vcodec:h264,acodec:aac,ext:mp4:m4a";
                    }
                    args.AppendFormat("-f \"{0}\" -S \"{1}\" --merge-output-format mp4 ", mp4Filter, mp4Sort);
                    break;
                }
                case DownloadMode.BestVideoAndAudioMkv:
                {
                    string mkvFilter = heightCap > 0
                        ? string.Format("bv*[height<={0}]+ba/b[height<={0}]/bv*+ba/b", heightCap)
                        : "bv*+ba/b";
                    args.AppendFormat("-f \"{0}\" --merge-output-format mkv ", mkvFilter);
                    break;
                }
                case DownloadMode.BestAudioOnlyMp3:
                    args.Append("-f \"ba/b\" -x --audio-format mp3 --audio-quality 0 ");
                    break;
                case DownloadMode.BestAudioOnlyM4a:
                    // Prefer native AAC/m4a stream so FFmpeg can fast-copy without re-encoding
                    args.Append("-f \"ba[acodec^=mp4a]/ba[ext=m4a]/ba/b\" -x --audio-format m4a --audio-quality 0 ");
                    break;
                case DownloadMode.BestAudioOnlyFlac:
                    args.Append("-f \"ba/b\" -x --audio-format flac --audio-quality 0 ");
                    break;
            }

            if (item.EmbedMetadata)
            {
                args.Append("--embed-metadata --embed-chapters ");
            }

            if (item.EmbedThumbnail)
            {
                args.Append("--embed-thumbnail --convert-thumbnails jpg ");
            }

            args.AppendFormat("-o \"{0}\" ", outputTemplate);
            args.AppendFormat("\"{0}\"", item.Url.Replace("\"", ""));

            return args.ToString();
        }
    }
}
