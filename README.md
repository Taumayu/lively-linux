# Lively Wallpaper Linux

A Linux port/prototype of the popular [Lively Wallpaper](https://github.com/rocksdanister/lively) application. This project demonstrates a decoupled daemon-UI architecture using gRPC and Unix Domain Sockets to bring animated desktop wallpapers to Linux (X11 & Wayland).

## Architecture

*   **Lively.Linux.Daemon**: A headless background service running as a gRPC server on a local Unix socket (`/tmp/lively-grpc.sock`). It acts as the "Engine Layer", receiving commands and controlling video playback.
*   **Lively.Linux.UI**: A modern desktop application built with [Avalonia UI](https://avaloniaui.net/). It acts as the "Control Layer" and gRPC client, allowing you to browse, download, and manage wallpapers.

## Features

*   **Online Gallery**: Browse and download community wallpapers directly from the Lively API.
*   **Local Library**: Automatically scans and organizes your downloaded wallpapers.
*   **Daemon Auto-Start**: The UI will automatically boot the daemon in the background if it isn't already running.
*   **VLC Integration**: Leverages `cvlc` (headless VLC) to draw video wallpapers natively to the root window/background.
*   **Wayland Support**: Uses `wlr-randr` to dynamically query available monitors on Wayland compositors (like Sway or Hyprland).
*   **System Autostart**: One-click toggle in the UI to generate a `.desktop` file for launching on boot.

## Requirements

*   [.NET 10.0 SDK](https://dotnet.microsoft.com/)
*   `vlc` (For video wallpaper playback via `cvlc`)
*   `wlr-randr` (Optional: for Wayland monitor detection)

## Building & Running

### 1. Build the project
```bash
dotnet build Lively.Linux.UI/Lively.Linux.UI.csproj
```

### 2. Run the Application
You only need to run the UI. The daemon will be automatically launched in the background.
```bash
dotnet run --project Lively.Linux.UI/Lively.Linux.UI.csproj
```

*If you prefer to run the daemon manually for debugging:*
```bash
dotnet run --project Lively.Linux.Daemon/Lively.Linux.Daemon.csproj
```

## How it works

1. The UI uses `Lively.Gallery.Client` to fetch wallpaper metadata.
2. Clicking **"Download"** fetches the `.zip` payload and extracts it to the local library.
3. Clicking **"Set Wallpaper"** sends the `LivelyInfo.json` path and Monitor ID via gRPC to the Daemon.
4. The Daemon parses the JSON, locates the video file, and executes `cvlc --video-wallpaper --loop --no-audio "/path/to/video.mp4"`.
5. Clicking **"Close Wallpaper"** sends a termination signal over gRPC to safely kill the VLC process.

## Future Roadmap

*   Implement a `WaylandWebPlayer` (via WebKitGTK or WPEWebKit) for HTML/Web wallpapers.
*   Support for multiple concurrent wallpapers on multi-monitor setups (spawning multiple player instances).
*   System Tray / AppIndicator support for the UI.
*   Playback pause rules (e.g., pausing playback when a fullscreen game is running to save resources).
