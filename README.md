<div align="center">

# Stash

A fast, lightweight **video and audio downloader** for **Windows**. Stash runs entirely on your PC, with **no accounts**, **no cloud**, and **no tracking**.

<a href="LICENSE.md"><img src="https://img.shields.io/badge/License-MIT-green.svg" alt="MIT License"></a>
<a href="https://github.com/thatveejaydev/Stash/releases/latest"><img src="https://img.shields.io/github/v/release/thatveejaydev/Stash" alt="Latest release"></a>
<img src="https://img.shields.io/badge/platform-Windows-blue" alt="Windows">
<img src="https://img.shields.io/badge/powered%20by-yt--dlp-red" alt="Powered by yt-dlp">
<img src="https://img.shields.io/badge/powered%20by-FFmpeg-green" alt="Powered by FFmpeg">

</div>

https://github.com/user-attachments/assets/c5a21e4c-a1b2-4c9e-9555-1b8483294d6b

## 🔋 Powered By

Stash is powered by two open-source tools:

- [**yt-dlp**](https://github.com/yt-dlp/yt-dlp) discovers and downloads **video**, **audio**, **playlists**, and other media from thousands of supported websites.
- [**FFmpeg**](https://ffmpeg.org/) **merges** video and audio streams, **converts** formats, **extracts** audio, and **embeds** metadata, thumbnails, and subtitles.

## ✨ Features

### Download from thousands of websites

- Download video and audio from [**websites**](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md) supported by yt-dlp, including YouTube, Instagram, TikTok, Facebook, Vimeo, and SoundCloud.
- Download individual videos, Shorts, playlists, albums, and other supported collections.
- Paste one link or multiple links at once.
- Click **Paste Link** or press `Ctrl+V` or `Shift+Insert` to add links and begin downloading.

### Choose your format and quality

- Save videos as **MP4** or **MKV**.
- Choose the best available quality or limit downloads to **4K, 1440p, 1080p, 720p, 480p, or 360p**.
- Extract audio as **MP3, M4A, FLAC, or WAV**. 
- Automatically combines separate video and audio into one finished file.

### Download playlists

- Download an entire playlist.
- Download only the individual video from a playlist link.
- Select a custom range, such as videos 5–20.

### Include extra media information

- Embed creator subtitles and automatic captions when available.
- Keep subtitles as optional tracks that can be turned on or off in a compatible player.
- Add titles, artist information, chapters, and other available details.
- Embed the video thumbnail as cover art.

### Manage download speed and activity

- Choose between **1, 4, 8, 12, or 16 download connections** for supported websites.
- Run between **one and six downloads simultaneously**.
- View progress, download speed, and estimated time remaining.
- Pause, resume, cancel, retry, or remove individual downloads.
- Clear completed downloads from the queue with one button.

### Organize downloaded files

- Choose where files are saved.
- Quickly reuse recently selected folders.
- Open a completed file’s location in File Explorer.
- Receive a warning when a similar file already exists.
- Rename the new file instead of accidentally replacing an existing one.

### Simple setup and updates

- Install the required download tools with one click.
- Check for Stash updates from the **About Stash** window.
- View release notes or install an available update and restart the application.
- Remember your preferred format, quality, folder, subtitles, and other options.

### Private and local

- Runs entirely on your Windows computer.
- Requires no Stash account.
- For videos that require a site login, use the toolbar's cookie menu to select a browser where you're already signed in. Firefox is recommended on Windows.
- Uses no cloud processing or tracking.

## 📥 Installation

1. Download the latest `Stash.exe` from the [Releases page](https://github.com/thatveejaydev/Stash/releases/latest).
2. Open `Stash.exe`.
3. Click the missing-engine status in the bottom bar to install yt-dlp and FFmpeg, or accept the installation prompt when pasting a link.
4. Copy a supported media link and select **Paste Link**.

Stash requires 64-bit Windows and an internet connection. No traditional installation is required.

## ⚙️ Settings and download tools

Stash stores your preferences and download tools in your Windows user folder. They remain available if you move `Stash.exe` elsewhere on the same computer.

When upgrading from an older version, Stash automatically moves compatible settings and tools to the new location. Each Windows account and computer keeps its own copy.

If the required tools are missing, click the missing-engine status to download them. Once installed, hover over **Engine Ready** for the update hint and click it to check for engine updates.

## 🖥️ Development

### Requirements

To build Stash from source, you need:

- Windows
- .NET Framework 4.x with the 64-bit C# compiler
- Git, if you want to clone the repository

No external packages are required to compile Stash. yt-dlp and FFmpeg are only needed when running the application and can be installed directly through Stash.

### Get the source code

```powershell
git clone https://github.com/thatveejaydev/Stash.git
cd Stash
```

### Build the application

Run the build script from Command Prompt or PowerShell:

```powershell
.\build.bat
```

The script compiles the source files and creates:

- `Stash.exe`
- `Stash.exe.sha256`
- Copies of both files inside `dist`, when that folder exists

Stash is compiled directly with the C# compiler included with .NET Framework. A Visual Studio solution is not required.

### Run the tests

```powershell
.\tests\run-tests.bat
```

The test program is created under `bin\tests` and runs automatically.

### Source structure

| Location | Purpose |
|---|---|
| `src\Application` | Application settings and download management |
| `src\Domain` | Download options, link handling, and download commands |
| `src\Infrastructure` | File storage, external tools, updates, and Windows integration |
| `src\UI` | Windows, dialogs, controls, and visual styling |
| `src\assets` | Images and other application assets |
| `tests` | Automated tests |
| `build.bat` | Application build script |

### Runtime tools

yt-dlp and FFmpeg are not required to compile Stash. They can be installed through the application when testing downloads.

## ⚠️ Disclaimer

Stash is intended for downloading content that you own or have permission to access. You are responsible for complying with applicable laws and each platform’s terms of service. The developers do not condone copyright infringement and are not responsible for misuse.

## 🙏 Show Your Support

Give a ⭐️ if this project helped you!

## 📄 License

Stash is available under the [MIT License](LICENSE.md).




