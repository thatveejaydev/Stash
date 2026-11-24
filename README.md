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
- Paste or drop one or more links into Stash, choose **Video**, **Audio**, or **Transcript**, then press Enter or click the download button.
- A batch uses the same settings for each link. Stash remembers your format choices for each mode.

### Choose your format and quality

- Save videos as **MP4** or **MKV**.
- Choose the best available quality or limit downloads to **4K, 1440p, 1080p, 720p, 480p, or 360p**.
- Extract audio as **MP3, M4A, FLAC, or WAV**. 
- Automatically combines separate video and audio into one finished file.
- Export source captions as **SRT**, **VTT**, or **TXT** with timestamps or plain paragraphs. For a single video, Stash shows which caption languages are available before downloading.

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
- Pause, resume, cancel, retry, or remove individual downloads, or pause and resume the whole queue at once.
- Close Stash mid-download and continue where you left off next time.
- Use the keyboard or right-click a download for quick actions.
- Clear completed downloads with **Clear** next to the Completed section.

### Organize downloaded files

- Choose where files are saved, and name files by title, title with video ID, or channel and title.
- Drag links onto the window, or let Stash pick up links you copy (optional).
- Quickly reuse recently selected folders.
- Open a completed file’s location in File Explorer. On Windows 11, it opens in a new tab of an open Explorer window with the file selected.
- Rename a finished download from its menu or with F2.
- Receive a warning when a similar file already exists.
- Rename the new file instead of accidentally replacing an existing one.

### Simple setup and updates

- Install the required download tools with one click. Downloads are checked against the publisher's SHA-256 checksums, and yt-dlp is kept up to date automatically.
- Check for Stash updates from the **About Stash** window.
- View release notes or install an available update and restart the application.
- Remember your preferred format, quality, folder, subtitles, and other options.

### Private and local

- Runs entirely on your Windows computer.
- Requires no Stash account.
- For videos that require a site login, use the cookie button at the bottom right (or **Cookies** under **More options**) to select a browser where you're already signed in. Firefox is recommended on Windows.
- Uses no cloud processing or tracking.

## 📥 Installation

1. Download the latest `Stash.exe` from the [Releases page](https://github.com/thatveejaydev/Stash/releases/latest).
2. Open `Stash.exe`.
3. Click the missing-engine status in the bottom bar to install yt-dlp and FFmpeg, or accept the installation prompt when pasting a link.
4. Copy a supported media link, paste it into the link field and click **Download**.

Stash requires 64-bit Windows and an internet connection. No traditional installation is required.

## ⚙️ Settings and download tools

Stash stores your preferences and download tools in your Windows user folder. They remain available if you move `Stash.exe` elsewhere on the same computer.

When upgrading from an older version, Stash automatically moves compatible settings and tools to the new location. Each Windows account and computer keeps its own copy.

If the required tools are missing, click the install message at the bottom of the window to download them. To update them later, open **About** (the info button at the bottom right) and click **Update engines**.

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

To code-sign the build, set `STASH_SIGN_PFX` (and `STASH_SIGN_PASSWORD`) before running the script; `signtool` must be on the PATH. Once releases are signed, the updater only accepts updates signed by the same publisher.

### Run the tests

```powershell
.\tests\run-tests.bat
```

The test program is created under `bin\tests` and runs automatically. GitHub Actions runs the tests and the build on every push and pull request.

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




