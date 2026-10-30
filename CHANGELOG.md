# Changelog

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