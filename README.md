<div align="center">

# Stash

Stash is a **lightweight**, **local** Video & Audio Downloader for Windows

<a href="https://choosealicense.com/licenses/mit/"><img src="https://img.shields.io/badge/License-MIT-green.svg" alt="MIT License"></a>
<img src="https://img.shields.io/badge/powered%20by-yt--dlp-red" alt="Powered by yt-dlp">
<img src="https://img.shields.io/badge/powered%20by-FFmpeg-green" alt="Powered by FFmpeg">

</div>

<p align="center">
  <img src="src/assets/stash ui.png" alt="Stash screenshot" width="700">
</p>

## 🔋 Powered By

Stash is built on top of two open-source tools:

- [**yt-dlp**](https://github.com/yt-dlp/yt-dlp): handles media extraction and downloading from supported sites.
- [**FFmpeg**](https://ffmpeg.org/): handles audio extraction, format conversion, and metadata/thumbnail embedding.

## ⚡ Effortless & Turbocharged Downloading

- **One-Click Paste & Auto-Download:** Click **Paste Link** or press `Ctrl+V` anywhere in the window to instantly queue any copied URL and start downloading immediately.
- **Multi-Connection Turbo Acceleration:** Bypasses server bandwidth throttling by splitting streams into chunks and downloading multiple fragments simultaneously (presets from `1x` up to `16x` maximum speed; `8x` enabled by default).
- **Parallel Downloading Queue:** Configure between `1` and `6` simultaneous downloads running in parallel with automatic background queue scheduling.
- **Pause & Resume Anytime:** Pause active downloads with a single click (`❚❚`) to free up bandwidth; click Resume (`▶`) to pick up right from where you left off without re-fetching or starting over.
- **Smart Cancel & Auto-Cleanup:** Cancelling a download (`✕`) immediately terminates active processes and cleans up all temporary `.part`, `.ytdl`, and fragment files, keeping your target folder completely clutter-free.

## 🎬 Maximum Quality & Instant Merging

- **Smart Muxing Without Re-encoding:** Prioritizes native H.264 video and AAC audio streams for MP4 downloads, allowing FFmpeg to merge files in ~1–2 seconds with 0% CPU strain and zero generational quality loss.
- **Adaptive Ultra-HD Support:** Automatically selects high-efficiency VP9/AV1 streams when downloading 2K (`1440p`) or 4K (`2160p`) media to guarantee maximum native resolution.
- **Flexible Video Containers (MP4 / MKV):** Choose between universal MP4 compatibility or open-container MKV across resolutions from `360p` up to `4K / 8K UHD`.
- **Lossless & Hi-Fi Audio Extraction:** Extract audio directly in `MP3 (320 kbps CBR)`, pristine native `M4A (AAC)`, or `FLAC (Lossless)`.

## 🏷️ Metadata, Organization & Windows Integration

- **Embedded Tags & Cover Art:** Automatically embed chapters, metadata tags (artist, title, album), and high-resolution JPG cover art directly into media containers.
- **Live Real-Time Telemetry:** Displays live download progress, ETA countdown, and connection bandwidth in real-time `Mbps` (megabits per second).
- **Smart Duplicate File Protection:** Scans your destination directory before downloading and prompts you with safe incremental renaming if a similar file already exists.
- **Built-in Engine Manager:** Automatic one-click download and update routine for `yt-dlp` and `FFmpeg` with self-healing local executable discovery.

---

## 📋 Summary

| Category | Features & Capabilities |
| :--- | :--- |
| **Speed & Engine** | Multi-Connection Turbo (`1x`–`16x`), Parallel Queues (1–6 concurrent), Smart Muxing (<2s stream-copy merge) |
| **Queue Controls** | Instant `Ctrl+V` paste, Pause (`❚❚`), Resume (`▶`), and Cancel (`✕`) with automatic fragment cleanup |
| **Video Quality** | Best / 4K UHD / 2K QHD / 1080p / 720p / 480p / 360p in MP4 or MKV |
| **Audio Quality** | MP3 (320 kbps), M4A (Original AAC), and FLAC (Lossless) |
| **Tags & Art** | Metadata, video chapters, and embedded high-res thumbnail cover art |
| **Workspace & UX** | Real-time `Mbps` speed meter, remembered folder history, duplicate file protection, and modern dark UI |

**In short:** Copy any link, press `Ctrl+V`, and let Stash handle the rest — fast, organized, and at the best quality available.

## 🔨 How to Build the Windows EXE

Run `build.bat` from Command Prompt or PowerShell:

```powershell
.\build.bat
```

This produces `dist\Stash.exe` and `Stash.exe`.

## 🙏 Show Your Support

Give a ⭐️ if this project helped you!