# Stash — Lightweight Windows Video & Audio Downloader

Stash is a fast, native, lightweight Windows desktop application (~76 KB binary, zero external runtime dependencies) powered by **yt-dlp** and **FFmpeg**.

## Features
- **One-Click Paste & Auto-Download**: Click **Paste Link** or press `Ctrl+V` anywhere in the window to immediately queue and start downloading.
- **Highest-Source-Quality Video & Audio**:
  - **Video (MP4 / MKV)**: Choose `Best` (up to 4K/8K HDR), `4K (2160p)`, `2K (1440p)`, `1080p`, `720p`, `480p`, or `360p`.
  - **Audio Only (MP3 320k / M4A / FLAC Lossless)**: Extracts the highest-bitrate audio stream directly using FFmpeg.
- **Metadata & Thumbnail Embedding**: Multi-select dropdown to embed chapters, metadata tags, and JPG cover art into your media files.
- **Persistent "Save to" Directory History**: Remembers your previously chosen folders (with one-click removal `✕` and full Windows File Explorer folder picker).
- **Duplicate Filename Detection & Rename Prompt**: Prompts you to rename before downloading if a file with a similar name already exists in the target directory.

## Project Structure
```text
MediaDownloader/
├── src/
│   ├── Program.cs        # Complete WinForms UI, queue manager, yt-dlp/FFmpeg process engine
│   └── app.manifest      # High-DPI Windows manifest
├── dist/
│   ├── Stash.exe         # Compiled standalone Windows executable
│   └── bin/              # Portable directory for yt-dlp.exe and ffmpeg.exe
├── Stash.exe             # Ready-to-run executable copy
├── build.bat             # One-click Windows EXE compiler script (uses built-in .NET csc.exe)
└── README.md
```

## How to Build the Windows EXE
Run `build.bat` from Command Prompt or PowerShell:
```powershell
.\build.bat
```
This produces `dist\Stash.exe` and `Stash.exe`.
