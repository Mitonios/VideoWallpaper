# Video Wallpaper — Đặc tả kỹ thuật

Tài liệu tham khảo chi tiết cho dự án và các dự án tương tự. Ghi lại mọi quyết định thiết kế, trick kỹ thuật và gotcha đã gặp.

---

## 1. Stack công nghệ

| Thành phần | Lựa chọn | Lý do |
| --- | --- | --- |
| Ngôn ngữ | C# 12 (.NET 8) | SDK-style project, top-level namespace, nullable enable |
| UI Framework | WPF | Native Windows, hardware-accelerated rendering, XAML |
| UI Theme | ModernWpfUI 0.9.6 | Dark mode Fluent style không cần tự viết style |
| Video playback | WPF `MediaElement` | Dùng Windows Media Foundation (WMF) / DirectShow, DXVA2 hardware decode tự động |
| Video loop | `MediaTimeline` + `Storyboard` | `RepeatBehavior.Forever` — loop không flash |
| Config | `System.Text.Json` | Built-in .NET 8, không cần package ngoài |
| Monitor detection | `System.Windows.Forms.Screen` | Trả về physical pixel bounds |
| Autostart | `Microsoft.Win32.Registry` | Ghi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| System tray | `System.Windows.Forms.NotifyIcon` | WPF không có tray built-in, phải dùng WinForms |
| IPC (singleton) | Win32 `RegisterWindowMessage` + `PostMessage` broadcast | Không cần named pipe, không cần FindWindow |
| DPI | PerMonitorV2 | `app.manifest` + `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` |
| Win32 P/Invoke | `user32.dll` | Desktop layer injection |
| Video optimization | ffmpeg (external process) | Async, progress bar, không phụ thuộc vào codec runtime |
| Versioning | `AssemblyVersion 1.0.*` | `<Deterministic>false</Deterministic>` — build number tự tăng |

---

## 2. Kiến trúc hai cửa sổ

### MainWindow (Settings UI)

- Kích thước cố định 440×510px, `ResizeMode=NoResize`
- Closing event bị cancel (`e.Cancel = true`) — chỉ ẩn, không đóng
- Chứa toàn bộ logic điều khiển: browse file, chọn monitor, toggle play, autostart, ffmpeg optimize
- Nhận event `MediaLoadFailed` từ `WallpaperWindow` để hiện lỗi codec

### WallpaperWindow (Video container)

- `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `ShowActivated=False`
- `Background=Black` — khi không có video thì đen, không thấy desktop
- Chứa duy nhất 1 `MediaElement`, `Stretch=UniformToFill`
- `LoadedBehavior=Manual`, `UnloadedBehavior=Stop`
- `RenderOptions.BitmapScalingMode=LowQuality` — ưu tiên performance, wallpaper không cần anti-alias
- `RenderOptions.EdgeMode=Aliased` — tương tự

**Thứ tự khởi tạo bắt buộc:**

```text
PositionOnMonitor()   ← set Left/Top/Width/Height trước khi Show
Show()                ← tạo HWND
GetHandle()           ← lấy HWND (EnsureHandle)
Attach()              ← SetParent vào WorkerW
PositionWindow()      ← SetWindowPos với physical pixels
PlayVideo()           ← bắt đầu phát
```

Nếu đảo thứ tự: HWND chưa tồn tại khi gọi `SetParent` → crash hoặc inject sai.

---

## 3. WorkerW Injection — Cơ chế cốt lõi

Đây là trick chính để video nằm đúng layer wallpaper của Windows.

### Cấu trúc cửa sổ desktop Windows

```text
Desktop
└── Progman (Program Manager)
    └── WorkerW
        └── SHELLDLL_DefView   ← chứa desktop icons
            └── SysListView32  ← listview icons thực sự
WorkerW (thứ 2)                ← đây là layer ta cần inject vào
```

Sau khi gửi message `0x052C` đến `Progman`, Windows tạo thêm một `WorkerW` thứ 2 nằm **sau** `WorkerW` chứa `SHELLDLL_DefView`. Layer này nằm phía sau icons nhưng phía trước wallpaper tĩnh.

### Thuật toán tìm WorkerW đúng

```csharp
SendMessageTimeout(progman, 0x052C, ...)   // kích hoạt tạo WorkerW thứ 2

EnumWindows(hwnd => {
    var shellView = FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null);
    if (shellView != 0) {
        // hwnd này chứa icons — WorkerW cần tìm là cửa sổ NGAY SAU nó
        workerW = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
    }
})
```

`FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null)` = tìm cửa sổ class "WorkerW" có Z-order **sau** `hwnd` trong danh sách global.

### DPI gotcha sau SetParent

Sau `SetParent(wpfHwnd, workerW)`, WPF coordinate system bị lệch vì window đã chuyển sang child của WorkerW. **Phải dùng Win32 `SetWindowPos` với physical pixel** thay vì set `Left/Top` của WPF window:

```csharp
// Child coords = screen coords - WorkerW origin
int childX = screenBounds.X - workerWRect.Left;
int childY = screenBounds.Y - workerWRect.Top;
SetWindowPos(hwnd, 0, childX, childY, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
```

`Screen.Bounds` trả về physical pixels. `workerWRect` lấy từ `GetWindowRect(workerW)`.

---

## 4. Video Playback — MediaElement & WMF

### Tại sao dùng MediaTimeline thay vì MediaElement trực tiếp

| Cách | Vấn đề |
| --- | --- |
| `MediaEnded` → `Position=0` → `Play()` | Flash trắng/đen ở điểm loop |
| `MediaTimeline` + `Storyboard` + `RepeatBehavior.Forever` | Loop mượt, không flash |

### Quy tắc LoadedBehavior

- `LoadedBehavior=Manual`: có thể gọi `Play()`, `Pause()`, `Stop()`, `Close()` trực tiếp
- `LoadedBehavior=Play`: WMF tự quản lý, **không được** gọi các method trên (throw exception)
- Dùng Storyboard điều khiển: cần `LoadedBehavior=Manual`, gọi `_storyboard.Begin()` / `_storyboard.Stop()`

### Codec support

WPF `MediaElement` dùng Windows Media Foundation trên .NET 8. Các codec được hỗ trợ sẵn:

- **H.264 (AVC)** trong MP4/MKV/AVI — luôn OK
- **H.265 (HEVC)** — cần "HEVC Video Extensions" từ Microsoft Store
- **VP9 / AV1** — không hỗ trợ natively, cần K-Lite Codec Pack
- **MKV container** — thường OK với H.264 bên trong

Lỗi codec: `MediaFailed` với `COMException 0xC00D109B` (`NS_E_WMP_UNSUPPORTED_FORMAT`).

### Hardware rendering hints

```xml
<MediaElement RenderOptions.BitmapScalingMode="LowQuality"
              RenderOptions.EdgeMode="Aliased" />
```

Cho phép GPU sử dụng đường dẫn render nhanh hơn, phù hợp với wallpaper (không cần pixel-perfect).

---

## 5. DPI Awareness

### Cấu hình

Hai nơi phải khai báo đồng bộ:

**`app.manifest`:**

```xml
<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
```

**`.csproj`:**

```xml
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

### Hệ quả

- `Screen.Bounds` trả về **physical pixels** (không scale)
- WPF `Left/Top/Width/Height` nhận **physical pixels** trực tiếp (không bị WPF DPI scaling)
- Sau `SetParent`, phải dùng Win32 `SetWindowPos` — xem mục 3

---

## 6. Singleton & IPC

### Mutex cho single instance

```csharp
_mutex = new Mutex(true, "VideoWallpaper_SingleInstance", out bool isNew);
if (!isNew) { /* instance đã chạy */ }
```

### IPC bằng RegisterWindowMessage + broadcast

Instance 2 không hiện messagebox mà gửi signal cho instance 1 mở Settings:

```csharp
// Cả 2 instance đăng ký cùng message name → cùng nhận được ID
uint msgId = RegisterWindowMessage("VideoWallpaper_ShowSettings");

// Instance 2: broadcast toàn hệ thống rồi tắt
PostMessage(HWND_BROADCAST, msgId, 0, 0);
Shutdown();

// Instance 1: HwndSource hook trong MainWindow nhận message
source.AddHook((hwnd, msg, wParam, lParam, ref handled) => {
    if ((uint)msg == _showSettingsMsg) {
        ShowSettings();
        handled = true;
    }
    return IntPtr.Zero;
});
```

`RegisterWindowMessage` đảm bảo ID là duy nhất toàn hệ thống, không conflict với app khác. Không cần `FindWindow` để tìm HWND của instance 1.

---

## 7. Explorer Restart Recovery

Khi `explorer.exe` crash hoặc restart, `WorkerW` handle bị invalidate. App lắng nghe broadcast `TaskbarCreated` (Windows gửi khi Explorer hoàn thành khởi động lại):

```csharp
uint WM_TASKBAR_CREATED = RegisterWindowMessage("TaskbarCreated");

// Trong HwndSource hook:
if ((uint)msg == WM_TASKBAR_CREATED) {
    // Delay 1s để Explorer hoàn tất khởi động
    timer.Interval = 1000ms;
    timer.Tick => _wallpaperService.ReAttach(hwnd);
}

// ReAttach:
_workerW = IntPtr.Zero;
GetWorkerW();   // tìm WorkerW mới
SetParent(wpfHwnd, _workerW);
```

HwndSource được gắn vào HWND của `MainWindow` (luôn tồn tại, không bị ẩn/đóng).

---

## 8. Autostart

```csharp
// Enable:
Registry.CurrentUser
    .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
    .SetValue("VideoWallpaper", $"\"{exePath}\" --minimized");

// Disable: DeleteValue("VideoWallpaper")
// Check: GetValue("VideoWallpaper") != null
```

Dùng `Environment.ProcessPath` để lấy đường dẫn exe hiện tại (không dùng `Assembly.GetExecutingAssembly().Location` — không đúng với self-contained publish).

Khi khởi động với `--minimized`: bỏ qua `_mainWindow.Show()`, phát video ngay nếu `isPlaying=true`.

---

## 9. Tối ưu Video (ffmpeg integration)

### Luồng

1. Kiểm tra ffmpeg có trong PATH: chạy `ffmpeg -version`, check `ExitCode == 0`
2. Build args: `-c:v libx264 -preset fast -crf 23 -vf scale='min(iw,1920)':'min(ih,1080)' -an -movflags +faststart`
3. Chạy async: `Process.Start()` + `await WaitForExitAsync()` — không block UI
4. Parse `stderr` để lấy thông báo lỗi nếu thất bại
5. Output: `{input_dir}/{input_name}_optimized.mp4`

### ffmpeg flags quan trọng

| Flag | Tác dụng |
| --- | --- |
| `-c:v libx264` | Encode H.264, WPF hỗ trợ native |
| `-preset fast` | Cân bằng tốc độ/chất lượng encode |
| `-crf 23` | Chất lượng (18=lossless, 28=thấp), 23 là mặc định |
| `scale='min(iw,1920)'` | Giới hạn 1920px width, không upscale |
| `-an` | Bỏ audio track — wallpaper không cần âm thanh, giảm ~30% kích thước |
| `-movflags +faststart` | Moov atom lên đầu file — WMF start nhanh hơn |

---

## 10. Auto-versioning

```xml
<!-- VideoWallpaper.csproj -->
<AssemblyVersion>1.0.*</AssemblyVersion>
<Deterministic>false</Deterministic>
<NoWarn>CS7035</NoWarn>
```

`1.0.*` = .NET tự tính:

- **Build** = số ngày kể từ 01/01/2000 (tăng mỗi ngày)
- **Revision** = số giây từ 00:00 chia 2 (tăng trong cùng ngày)

Đọc lại lúc runtime:

```csharp
var ver = Assembly.GetExecutingAssembly().GetName().Version;
Title = $"Video Wallpaper v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
// → "Video Wallpaper v1.0.9581"
```

---

## 11. Debug Logging

```csharp
// %LocalAppData%\VideoWallpaper\debug.log
// Reset mỗi lần startup
DebugLogger.Log("message");
DebugLogger.LogError("context", exception);
```

Key lines cần check khi debug:

| Log entry | Ý nghĩa |
| --- | --- |
| `WorkerW: 0x00000000` | Injection thất bại (VM, third-party shell) |
| `SetParent failed! Win32Error=5` | Access denied |
| `MediaFailed! COMException: 0xC00D109B` | Codec không hỗ trợ |
| `PositionOnMonitor` bounds vs WPF coords | Mismatch = DPI issue |
| `SetWindowPos => ok=False` | Lỗi sau SetParent |

---

## 12. Cấu trúc project

```text
VideoWallpaper/
├── App.xaml / App.xaml.cs          Entry point, singleton, IPC, tray, explorer recovery
├── MainWindow.xaml / .cs           Settings UI, ffmpeg optimize, điều khiển playback
├── WallpaperWindow.xaml / .cs      Video container, MediaElement, Storyboard
├── Services/
│   ├── WallpaperService.cs         WorkerW injection (P/Invoke)
│   ├── ConfigService.cs            JSON config read/write
│   ├── MonitorService.cs           Screen enumeration
│   └── AutostartService.cs         Registry autostart
├── Models/
│   └── AppConfig.cs                Config model (VideoPath, MonitorDevice, IsPlaying, Autostart)
├── VideoWallpaper.csproj           PerMonitorV2, AssemblyVersion 1.0.*
└── app.manifest                    DPI awareness declaration
```

---

## 13. Checklist cho dự án tương tự

- [ ] `dotnet new wpf -n AppName`
- [ ] Thêm `app.manifest` với PerMonitorV2 DPI + khai báo trong `.csproj`
- [ ] Thêm `<UseWindowsForms>true</UseWindowsForms>` cho `Screen` + `NotifyIcon`
- [ ] Thêm `ModernWpfUI` nếu muốn dark mode Fluent
- [ ] Implement singleton bằng `Mutex` + `RegisterWindowMessage` IPC
- [ ] Tạo `WallpaperWindow`: `WindowStyle=None`, `ShowInTaskbar=False`
- [ ] `WorkerW` injection: `SendMessageTimeout(0x052C)` → `EnumWindows` → `SetParent`
- [ ] Sau `SetParent`: dùng `SetWindowPos` với physical pixels (không dùng WPF `Left/Top`)
- [ ] `MediaTimeline` + `RepeatBehavior.Forever` cho loop không flash
- [ ] `MediaFailed` event để bắt lỗi codec, hiện thông báo rõ ràng
- [ ] Hook `TaskbarCreated` qua `HwndSource` để re-attach khi explorer restart
- [ ] Autostart: `Environment.ProcessPath` + `--minimized` arg
- [ ] `AssemblyVersion 1.0.*` + `Deterministic=false` cho auto-increment build
- [ ] Debug log file reset mỗi startup
