# Zbotipy: Local Windows music player with synced lyrics and Discord presence

## Overview

Zbotipy is a **Windows desktop** music player built with **WPF** and **.NET 8**. It plays local **MP3** libraries, supports **LRC** lyrics (in the main window and a floating lyrics window), optional **Discord Rich Presence**, and reads track metadata with **TagLibSharp**. Playback uses **NAudio**.

> **Personal build:** This version is hard-coded for use on my own PC. It is the first working snapshot and is **not** set up for other people to run as-is (paths, assumptions, and polish are for my environment only).

## Features

- [x] Playlist-based library
- [x] Playback controls
- [x] Synced lyrics in the main window
- [x] Synced lyrics in always-on-top floating lyrics window
- [x] Discord Rich Presence integration
- [x] Minimize-to-tray

## Project layout

```text
LocalMusicPlayer/
  Models/           # Track, playlist, lyric line models
  Services/         # Audio, lyrics, playlists, Discord, preferences
  ViewModels/       # Main view model
  Views/            # MainWindow, LyricsWindow, converters
```

## Notice

Seems like I'm backing to China soon and will not have a working Windows computer. The development of this application will stay as what it is right now till I back to the US in August.

## Contributing

CURSOR & @LiuZhenz701
