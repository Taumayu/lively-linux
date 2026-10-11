# Lively Wallpaper Linux

A Linux port/prototype of the popular [Lively Wallpaper](https://github.com/rocksdanister/lively) application. This project demonstrates a decoupled daemon-UI architecture using gRPC and Unix Domain Sockets to bring animated desktop wallpapers to Linux (X11 & Wayland).

## Architecture

*   **Lively.Linux.Daemon**: A headless background service running as a gRPC server on a local Unix socket (`/tmp/lively-grpc.sock`). It acts as the "Engine Layer", receiving commands and controlling video playback.
*   **Lively.Linux.UI**: A modern desktop application built with [Avalonia UI](https://avaloniaui.net/). It acts as the "Control Layer" and gRPC client, allowing you to browse, download, and manage wallpapers.

## Features

*   **Official Lively Wallpapers**: Bundled with the 12 official Lively wallpapers (Fluids, Matrix Rain, Medusae, The Hill, Periodic Table, Parallax.js, Rain, Triangles & Light, etc.).
*   **Web & WebGL Wallpapers**: Native playback of interactive HTML5, WebGL, Canvas, and CSS3 wallpapers via `WaylandWebPlayer` (WebKitGTK).
*   **VLC Video Playback**: Leverages VLC (`cvlc`) to draw video wallpapers (`.mp4`, `.webm`, `.gif`) natively to the root background window.
*   **Composite Wallpaper Engine**: Automatically routes wallpapers to the appropriate player (Web vs Video) based on file format and metadata.
*   **Online Gallery**: Browse and download community wallpapers directly from the Lively API.
*   **Local Library**: Automatically scans, organizes, and auto-populates default wallpapers.
*   **Daemon Auto-Start**: The UI will automatically boot the daemon in the background if it isn't already running.
*   **Wayland Support**: Full KWin Wayland window rules integration, plus `wlr-randr` and `kscreen-doctor` monitor detection.
*   **System Autostart**: One-click toggle in the UI to generate a `.desktop` file for launching on boot.


## Requirements

*   [.NET 10.0 SDK](https://dotnet.microsoft.com/)
*   `vlc` (For video wallpaper playback via `cvlc`)
*   `python-gobject` and `webkit2gtk-4.1` (For Web/HTML5/WebGL wallpaper playback)
*   `kscreen-doctor` or `wlr-randr` (For Wayland monitor detection)

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

1. The Local Library loads all default bundled Lively wallpapers as well as downloaded wallpapers from `~/.local/share/lively-linux/wallpapers`.
2. Clicking **"Set Wallpaper"** sends the `LivelyInfo.json` path and Monitor ID via gRPC to the Daemon.
3. The Daemon parses the metadata and routes the wallpaper through `CompositeWallpaperPlayer`:
   - **Web / HTML5 / WebGL / Canvas / Picture**: Executed via `WaylandWebPlayer` using a borderless WebKitGTK instance.
   - **Video / GIF**: Executed via `WaylandVlcPlayer` (or `WaylandMpvPlayer`).
4. Wayland desktop rules (KWin rules & scripting) pin the player window to the desktop background layer behind all application windows.
5. Clicking **"Close Wallpaper"** safely terminates any active wallpaper player.

## Future Roadmap

*   Support for multiple concurrent wallpapers on multi-monitor setups (spawning multiple player instances).
*   System Tray / AppIndicator support for the UI.
*   Playback pause rules (e.g., pausing playback when a fullscreen game is running to save resources).
