# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                                          # Build
dotnet run                                            # Build + run
dotnet publish -c Release -r win-x64 --self-contained # Publish portable exe
```

VS Code task `Ctrl+Shift+B` runs `dotnet run` directly.

## Architecture

### The wallpaper injection trick

The core mechanism is Win32 desktop layer injection:

1. Send message `0x052C` to the `Progman` window → forces Windows to create a `WorkerW` child window
2. `EnumWindows` to find the `WorkerW` that comes **after** the window containing `SHELLDLL_DefView` (the desktop icon layer)
3. `SetParent(wallpaperHwnd, workerW)` — embeds the WPF window at the correct layer: behind desktop icons, in front of the static wallpaper

This is implemented in `Services/WallpaperService.cs`. If `GetWorkerW()` returns `IntPtr.Zero`, the injection fails silently.

### Two-window design

- **`MainWindow`** — settings UI (440×460px, fixed). Closing hides it (`e.Cancel = true`), app lives in system tray.
- **`WallpaperWindow`** — the fullscreen video container, no border, no taskbar button. Shown/hidden by `StartPlayback()`/`StopPlayback()`.

`WallpaperWindow` must be `Show()`n and have its HWND created **before** `SetParent` is called. Call order matters: `PositionOnMonitor` → `Show` → `GetHandle` → `Attach`.

### Video playback

Uses `MediaTimeline` in a `Storyboard` with `RepeatBehavior.Forever` for seamless looping (avoids flash on repeat). `MediaElement.LoadedBehavior` must be `Play` (not `Manual`) when controlled by a Storyboard.

### Monitor coordinates

`System.Windows.Forms.Screen.Bounds` returns **physical pixel** coordinates. WPF window `Left`/`Top`/`Width`/`Height` are set to these values directly. The project targets `PerMonitorV2` DPI awareness (`<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` in csproj + `app.manifest`), so WPF window properties accept physical pixel coordinates.

### Explorer restart recovery

`App.xaml.cs` hooks `HwndSource` on the `MainWindow` handle to receive the `TaskbarCreated` broadcast message (sent when `explorer.exe` restarts). On receipt, `WallpaperService.ReAttach()` is called after a 1s delay to re-run the full WorkerW injection sequence.

### Startup argument

`--minimized` arg: app starts playing immediately without showing `MainWindow`. Written to registry by `AutostartService` at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Runtime file locations

| File | Path |
|---|---|
| Config | `%AppData%\VideoWallpaper\config.json` |
| Debug log | `%LocalAppData%\VideoWallpaper\debug.log` |

## Debugging

The app writes a detailed log to `%LocalAppData%\VideoWallpaper\debug.log` (reset on each startup). A "Xem Log" button in the settings UI opens it directly. Key things to check in the log:

- `WorkerW: 0x00000000` → injection failed; may occur in VMs or with some third-party shells
- `SetParent failed! Win32Error=...` → permissions issue
- `[ERROR] MediaFailed!` → codec missing or bad file path
- `PositionOnMonitor` bounds vs actual `WallpaperWindow` WPF coordinates — mismatch = DPI issue
