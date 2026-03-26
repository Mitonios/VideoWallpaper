# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained
```

VS Code / Cursor: `Ctrl+Shift+B` chạy `dotnet run` trực tiếp (task định nghĩa trong `.vscode/tasks.json`).

## Architecture

### Two-window design

- **`MainWindow`** (440×510px, fixed) — Settings UI. Closing hides it (`e.Cancel = true`), app lives in system tray. Contains all control logic: browse file, monitor select, play toggle, autostart, ffmpeg optimize.
- **`WallpaperWindow`** — fullscreen video container, no border, no taskbar button. `ShowActivated=False`. Shown/hidden by `StartPlayback()`/`StopPlayback()`.

### WorkerW injection (the wallpaper trick)

Sequence in `Services/WallpaperService.cs`:

1. `SendMessageTimeout(Progman, 0x052C)` → forces Windows to create a second `WorkerW`
2. `EnumWindows` → find the `WorkerW` that comes **after** the window containing `SHELLDLL_DefView`
3. `SetParent(wallpaperHwnd, workerW)` → embeds WPF window at correct layer (behind icons, in front of static wallpaper)

If `GetWorkerW()` returns `IntPtr.Zero`, injection fails silently.

### Call order matters

```text
PositionOnMonitor()   ← set WPF Left/Top/Width/Height
Show()                ← creates HWND
GetHandle()           ← EnsureHandle
Attach()              ← SetParent into WorkerW
PositionWindow()      ← SetWindowPos with physical pixels
PlayVideo()           ← start Storyboard
```

After `SetParent`, WPF coordinate system breaks. Must use Win32 `SetWindowPos` with physical pixel coordinates (screen coords minus WorkerW origin from `GetWindowRect`).

### Video playback

`MediaTimeline` in a `Storyboard` with `RepeatBehavior.Forever` — seamless loop, no flash on repeat. `LoadedBehavior=Manual` required; calling `VideoPlayer.Close()` in `StopVideo()` requires this. Do **not** switch to `LoadedBehavior=Play`.

### DPI awareness

`PerMonitorV2` declared in both `app.manifest` and `<ApplicationHighDpiMode>` in csproj. `Screen.Bounds` returns physical pixels. WPF window properties accept physical pixel values directly.

### Singleton & IPC

Mutex `"VideoWallpaper_SingleInstance"` for single instance. Second instance uses `RegisterWindowMessage("VideoWallpaper_ShowSettings")` + `PostMessage(HWND_BROADCAST)` to signal the first instance to show Settings, then exits. First instance hooks this message via `HwndSource` on `MainWindow`.

### Explorer restart recovery

`HwndSource` on `MainWindow` HWND hooks `WM_TASKBAR_CREATED` (broadcast when `explorer.exe` restarts). On receipt, `WallpaperService.ReAttach()` is called after 1s delay to re-run full WorkerW injection.

### Auto-versioning

`<AssemblyVersion>1.0.*</AssemblyVersion>` + `<Deterministic>false</Deterministic>`. Build number = days since 2000-01-01, auto-increments per day. Read at runtime via `Assembly.GetExecutingAssembly().GetName().Version`, displayed in title bar.

### ffmpeg video optimization

`OptimizeButton_Click` in `MainWindow.xaml.cs`: checks `ffmpeg` in PATH, runs async process with `-c:v libx264 -preset fast -crf 23 -vf scale='min(iw,1920)':'min(ih,1080)' -an -movflags +faststart`, outputs `{name}_optimized.mp4`. Progress bar shown during processing.

### Startup argument

`--minimized`: skip showing `MainWindow`, start playback immediately. Written to registry by `AutostartService` at `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Runtime file locations

| File | Path |
| --- | --- |
| Config | `%AppData%\VideoWallpaper\config.json` |
| Debug log | `%LocalAppData%\VideoWallpaper\debug.log` |

## Debugging

Log resets on each startup. Key entries:

| Entry | Meaning |
| --- | --- |
| `WorkerW: 0x00000000` | Injection failed (VM or third-party shell) |
| `SetParent failed! Win32Error=5` | Permissions issue |
| `MediaFailed! COMException: 0xC00D109B` | Codec missing or unsupported format |
| `SetWindowPos => ok=False` | DPI/coordinate issue after SetParent |

## Codec support

`MediaElement` uses Windows Media Foundation. MP4 H.264 works natively. MKV/H.265 may require codec pack. `MediaFailed` event shows user-friendly error with codec install hint.
