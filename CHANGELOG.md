# Changelog

## v0.2.8

### New Features
- **Transcript Downloads:** A new **Transcript** mode saves a video's captions as SRT, VTT, TXT with timestamps, or TXT as plain paragraphs. Repeated lines from rolling auto-captions are removed from TXT exports.
- **Caption Languages:** For a single video, Stash checks which captions exist before downloading, lists each language as **Creator** or **Auto**, and says whether the chosen language is available. **Original** picks the video's own language; batches can choose from common languages instead.
- **Transcript Files:** Transcripts are saved as "Title [language]" next to your other downloads, open in Notepad from the queue, and keep their language and format when the queue is restored.

### Redesign
- **Mode Picker:** Choose **Video**, **Audio**, or **Transcript** under the link field. Each mode remembers its own format, and the download button names what it will download.
- **Settings Beside the Mode:** Format, quality (or caption language and transcript format), and the save folder each have their own dropdown, replacing the single preset chip. The gear opens the remaining options and hides those that don't apply to transcripts.
- **Matching Dropdowns:** Format, quality and language menus share the folder menu's style. Long language lists scroll.
- **Rounded Controls:** The mode picker is one rounded switch, and dropdown buttons have clean rounded corners.
- **Compact Header:** The link field and controls take less space. The hint line is gone; status messages only appear when there is something to report.

### Improvements & Fixes
- **Paste Shortcuts:** Ctrl+V or Shift+Insert anywhere in the window downloads the copied links right away; inside the link field they paste normally. Dropped links go into the link field so you can pick a mode first, and pressing Enter with an empty field still uses the clipboard.
- **Inline Messages:** Invalid links, an empty clipboard, and engine preparation are reported under the link field instead of in pop-up windows.
- **Partial Batches:** If only some links in a batch are queued, the rest stay in the field so they can be submitted again.
- **Duplicate Links:** The same link can be queued as a video and as a transcript, or as transcripts in different languages or formats.
- **Caption Errors:** Missing captions and caption files without readable text show their own explanation and advice.

## v0.2.7

### Redesign
- **One Link Field:** A single field replaces the Paste Link button and the toolbar. Paste or type a link and press Enter or **Download**; an empty field downloads from the clipboard.
- **Two Settings Menus:** The chip in the link field (for example "MP4 · Best · Downloads") sets format, quality and folder. **More options** holds subtitles, embedding, parallel downloads, speed boost, file names, cookies and clipboard watching. The cookie button in the footer opens only the browser cookie choices.
- **Grouped Queue:** Downloads are split into **In progress** and **Completed**, with **Pause all/Resume all** and **Clear** next to each section instead of a row of buttons.
- **Quieter Cards:** Each card shows its own buttons (pause, cancel, retry, details). Completed downloads show open, folder and more buttons on hover.
- **Calmer Footer:** The footer shows a short queue summary and only mentions the engines when they need attention. Engine updates moved to **About**.
- **Consistent Dialogs:** About, error details, rename, playlist and quit dialogs share one layout, button style and sentence-case wording.
- **Smoother Menus:** Dropdowns and the right-click menu appear fully drawn in one frame, without flashing while you move between options.

### New Features
- **Resume After Closing:** Closing Stash during downloads asks whether to pause them and continue next time, cancel them, or keep downloading. The queue, including finished history, is restored on the next launch.
- **Drag and Drop:** Drop links, text, or `.url` shortcuts onto the window to queue them.
- **Queue Keyboard and Menu:** Use arrow keys, Space (pause/resume), Enter (open file or error details), Del (cancel/remove), Ctrl+C (copy link), and a right-click menu on each card. Double-click a finished card to open the file.
- **File Name Style:** Choose Title, Title [ID], or Channel - Title under **More options**.
- **Clipboard Watching (opt-in):** Under **More options** → **Clipboard**, Stash can queue links you copy in other apps.
- **Notifications:** The taskbar button shows queue progress, and a notification appears when the queue finishes while Stash is in the background.
- **Engine Updates:** yt-dlp is checked for updates once a day when nothing is downloading. FFmpeg is refreshed when it is over 30 days old, and engine downloads show progress.

### Improvements & Fixes
- **Drive Roots:** Downloads to a drive root such as `D:\` no longer fail or get stuck.
- **Verified Engines:** yt-dlp and FFmpeg are checked against the publisher's SHA-256 list and test-run before replacing the installed copy. Interrupted downloads can no longer leave a broken engine.
- **Clearer Errors:** Failure reasons use yt-dlp's exact wording, so network errors are no longer reported as access problems. Rate limits, unsupported sites and unavailable formats have their own advice. Error details no longer include stack traces.
- **Playlists:** Playlists with private or deleted videos can be queued (skipped videos are counted). Large playlists get up to 3 minutes, with a Retry button. The linked video can always be queued on its own, and custom ranges default to the full list.
- **Batch Paste:** Playlists pasted among other links each open the playlist picker instead of downloading as one item. Links already in the queue are skipped.
- **Duplicates:** Duplicate detection no longer matches titles that merely start the same way, also covers playlist items and other queued downloads, and no longer freezes in large folders.
- **Reliability:** Settings and the queue are saved atomically. Closing Stash can no longer hang on a stuck engine process. Updating engines waits for downloads to stop. Leftover temporary folders are hidden and cleaned up.
- **Signed Updates:** When releases are code-signed, updates must be signed by the same publisher.

## v0.2.6

### Improvements & Fixes
- **Window Opening:** Buffered painting helps prevent a brief white flash when opening Stash and its dialogs.
- **Playlist Detected:** Custom range fields start at 0. Queue becomes available after the full playlist loads, and custom ranges must be valid. The dialog title now reads **Playlist Detected**.

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
