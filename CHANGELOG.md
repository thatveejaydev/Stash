# Changelog

## v0.2.5

### New Features
- **Browser Cookies:** Use the toolbar's cookie menu to choose a signed-in browser for playlist previews, metadata, and downloads that require an account. Firefox is recommended on Windows.
- **Queue Thumbnails:** Download cards show widescreen artwork from the source when available.

### Improvements & Fixes
- **High-Quality MP4:** Best quality, 1440p, and 4K prefer AV1 over VP9 at the same resolution. MP4 still favors AAC audio and prefers H.264 at 1080p and below.
- **Format Labels:** The MKV option is now labeled **Best Source Quality**.
- **Smoother Progress:** The progress bar eases toward each update and shows moving diagonal stripes during active downloads.
- **Toolbar Options:** Subtitles, Embed, Parallel, and Turbo are grouped after the Downloads selector. Changing Parallel, Turbo, or the browser keeps its menu open.
- **About Stash:** Buffered painting prevents the brief white flash when opening the dialog.

## v0.2.4

### New Features
- **WAV Audio:** Download audio as 16-bit PCM WAV while preserving the source sample rate and channel count. WAV skips unsupported cover-art and chapter embedding.
- **Completed Download Details:** Finished cards now show duration, actual file size, format, resolution, and frame rate where available; audio downloads omit video-only information.

### Improvements & Fixes
- **Consistent Status Labels:** Active download labels now end with three dots, such as "Checking source..." and "Downloading video...".
- **Cleaner Footer:** Removed the active-download count and separate Update Engine button. Engine Ready remains clickable and explains engine updates in a tooltip.
- **App Information:** Centered the information icon and added a subtle version label beside it.
- **Readable Engine Updates:** Engine-update text stays readable while busy, with a wait cursor and protection against duplicate clicks.

## v0.2.3

### Improvements & Fixes
- **About Stash:** Open the new compact information dialog from the **ⓘ** button to see the installed version and update controls.
- **Manual Update Checks:** Check for updates only when you click **Check for Updates** inside About Stash. Stash no longer checks automatically on startup or when opening the dialog.
- **Simpler Release Access:** **View Release** opens release notes on GitHub. The in-app release-notes panel and preview toggle have been removed.
- **GitHub Support:** Added a **Give a star on GitHub** prompt with a centered yellow star button that opens the repository.
- **Cleaner Window Title:** The title bar now shows **Stash**; the version is displayed in About Stash.

## v0.2.2

### Improvements & Fixes
- **Update Version Verification:** Reject release executables whose embedded version does not match the release tag, preventing repeated update prompts caused by an outdated release asset.
- **Portable Update Reliability:** Safely replace portable copies stored on another drive, wait for Stash to exit before replacement, and restore the previous executable if the updated application fails to start.

## v0.2.1

### New Features
- **In-App Updates:** Stash checks for new stable releases on startup. Use **Check for Updates** to check manually, view release notes, and download a verified update before restarting.

### Improvements & Fixes
- **YouTube Shorts:** Pasting an individual Shorts link now downloads the video directly instead of opening the playlist picker. Channel Shorts tabs and playlists still open the picker.
- **Version Metadata:** The application and Windows executable metadata now consistently report the release version.

## v0.2.0

### New Features
- **Embed Subtitles:** Download videos with subtitles built in (when available). Stash fetches both creator-uploaded and auto-generated captions and embeds them as separate subtitle tracks inside the video file, so you can switch between them in your player.
- **Batch Link Paste:** Paste multiple links at once instead of adding them one by one.
- **Playlist Range Picker:** Choose exactly which part of a playlist to download, or grab the whole thing.

### Improvements & Fixes
- **Unified download manager:** Download controls are now handled in one place, making downloads more reliable and consistent.
- **Cleaner install folder:** The download engines (yt-dlp and ffmpeg) and Settings config file are now stored in your local AppData folder, so no extra files sit next to the app's `.exe`.
- **Smoother downloads (UI):** The queue now updates about 5 times per second instead of on every progress message, so the app stays responsive even with many downloads running.
- **Correct status labels:** Download cards now show the right label for each stage, such as "Downloading video" and "Downloading audio."
- **Faster queue updates:** When a download progresses, only that download's card is redrawn instead of the entire queue.
- **Limited log size:** The log keeps only the latest 1,000 lines, which prevents memory use from growing over time and keeps the log box fast.
- **Less disk activity:** Logs are now written to disk in small batches in the background instead of line by line.
- **Better error display:** Errors are easier to read in the log.
