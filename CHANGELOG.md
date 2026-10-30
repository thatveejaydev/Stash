# Changelog

## v0.2.0

### New Features
- **Embed Subtitles:** Download videos with subtitles built in (when available). Stash fetches both creator-uploaded and auto-generated captions and embeds them as separate subtitle tracks inside the video file, so you can switch between them in your player.
- **Batch Link Paste:** Paste multiple links at once instead of adding them one by one.
- **Playlist Range Picker:** Choose exactly which part of a playlist to download, or grab the whole thing.

### Improvements & Fixes
- **Unified download manager:** Download controls are now handled in one place, making downloads more reliable and consistent.
- **Cleaner install folder:** The download engines (yt-dlp and ffmpeg) and Settings config file are now stored in your local AppData folder, so no extra files sit next to the app's `.exe`.