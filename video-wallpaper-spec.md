# Video Wallpaper — Đặc tả kỹ thuật & UI/UX

## 1. Tổng quan

Ứng dụng desktop Windows cho phép đặt một file video làm hình nền động, hoạt động giống hệt wallpaper tĩnh mặc định của Windows — luôn nằm dưới các icon desktop và tất cả các cửa sổ khác. App chạy nền dưới dạng system tray utility, không chiếm taskbar.

---

## 2. Stack công nghệ

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | C# (.NET 8) |
| UI framework | WPF (Windows Presentation Foundation) |
| Video rendering | `MediaElement` (WPF built-in) — dùng DirectShow/Media Foundation, hardware decode DXVA2 |
| IDE | Cursor (với C# Dev Kit extension) + dotnet CLI |
| Build | `dotnet build` / `dotnet run` |
| Config storage | JSON tại `%AppData%\VideoWallpaper\config.json` |
| Autostart | Registry key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |

---

## 3. Kiến trúc kỹ thuật

### 3.1 Cửa sổ video wallpaper

- Tạo một `Window` WPF riêng biệt (gọi là `WallpaperWindow`) chứa `MediaElement` fullscreen.
- Dùng Windows API (`SetParent` / `FindWindowEx`) để gắn `WallpaperWindow` vào handle `WorkerW` — đây là layer nằm phía sau các icon desktop nhưng phía trên wallpaper tĩnh, đúng với hành vi wallpaper mặc định của Windows.
- `WallpaperWindow` không có border, không có taskbar button (`ShowInTaskbar = false`), không thể focus hay tương tác.

### 3.2 Chọn màn hình

- Dùng `System.Windows.Forms.Screen.AllScreens` để lấy danh sách monitor.
- Đặt `WallpaperWindow.Left`, `WallpaperWindow.Top`, `Width`, `Height` theo bounds của màn hình được chọn.
- Lưu monitor theo `DeviceName` (ví dụ `\\.\DISPLAY2`) thay vì index để tránh lệch khi thay đổi cấu hình màn hình.

### 3.3 Vòng lặp video

- `MediaElement.LoadedBehavior = Manual`, `MediaElement.UnloadedBehavior = Stop`.
- Dùng `MediaTimeline` với `RepeatBehavior = RepeatBehavior.Forever` để loop mượt, tránh flash ở điểm loop khi xử lý event thủ công.
- Khi toggle off: gọi `Stop()`, ẩn `WallpaperWindow`, Windows tự hiển thị lại wallpaper tĩnh gốc.

### 3.4 System tray

- Dùng `System.Windows.Forms.NotifyIcon` để tạo tray icon.
- Context menu chuột phải: **Mở cài đặt** / **Thoát**.
- Click đơn vào tray icon → hiện `SettingsWindow`.
- Đóng `SettingsWindow` (nhấn X) → chỉ ẩn cửa sổ, không thoát app (`e.Cancel = true` trong `Closing` event).

### 3.5 Autostart

- Khi checkbox được bật: ghi `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` với giá trị là đường dẫn executable + argument `--minimized`.
- Khi app khởi động với argument `--minimized`: bỏ qua việc hiện `SettingsWindow`, phát video luôn và thu nhỏ xuống tray.

### 3.6 Config

```json
{
  "videoPath": "C:\\Users\\user\\Videos\\nature_loop.mp4",
  "monitorDevice": "\\\\.\\DISPLAY1",
  "isPlaying": true,
  "autostart": true
}
```

Đọc config khi khởi động. Nếu `isPlaying = true` → phát video ngay, không cần tương tác.

---

## 4. Luồng logic ứng dụng

### 4.1 Lần đầu mở app (chưa có config)

```
Khởi động
  → Không tìm thấy config.json
  → Hiện SettingsWindow
  → Người dùng chọn file video
  → Chọn màn hình (nếu có nhiều)
  → Bật toggle (hoặc để mặc định off)
  → Nhấn "Lưu cài đặt"
  → Ghi config.json
  → Phát video ngay nếu toggle = on
  → App thu vào system tray
```

### 4.2 Các lần mở tiếp theo

```
Khởi động
  → Đọc config.json
  → Nếu isPlaying = true → phát video ngay
  → App nằm ở system tray, không hiện UI
```

### 4.3 Khởi động cùng Windows (autostart)

```
Windows boot
  → App khởi động với --minimized
  → Đọc config.json
  → Phát video ngay (nếu isPlaying = true)
  → Không hiện SettingsWindow
  → Chỉ hiện tray icon
```

### 4.4 Thay đổi cài đặt

```
Click tray icon → Mở SettingsWindow
  → Thay đổi file / màn hình / toggle / autostart
  → Nhấn "Lưu cài đặt"
  → Ghi config.json
  → Áp dụng thay đổi ngay lập tức (không cần restart)
  → Đóng SettingsWindow → app tiếp tục chạy nền
```

---

## 5. UI / UX — Cửa sổ Settings

### 5.1 Cấu trúc cửa sổ

- **Kích thước:** cố định, khoảng 440×420px, không thể resize.
- **Titlebar:** tiêu đề "Video Wallpaper", không có maximize button.
- **Đóng cửa sổ (X):** chỉ ẩn cửa sổ, không thoát app.
- Chia thành 4 section rõ ràng, phân cách bởi đường kẻ mỏng.

---

### 5.2 Section 1 — Video

**Label section:** `VIDEO`

| Control | Mô tả |
|---|---|
| Label `File nguồn` | Text cố định bên trái |
| Text display (readonly) | Hiển thị tên file đang chọn (chỉ tên file, không phải full path). Placeholder mờ "Chưa chọn file" khi chưa có. |
| Nút `Browse…` | Mở `OpenFileDialog`, filter: `Video files (*.mp4;*.mkv;*.avi;*.mov)`. Khi chọn xong → cập nhật display ngay. Nếu video đang phát → reload file mới ngay lập tức, không cần nhấn Save. |

---

### 5.3 Section 2 — Màn hình

**Label section:** `MÀN HÌNH`

| Control | Mô tả |
|---|---|
| Label `Hiển thị trên` | Text cố định bên trái |
| Dropdown | Liệt kê tất cả monitor theo format: `Display N — [Tên màn hình] (WidthxHeight)`. Ví dụ: `Display 1 — Primary (2560×1440)`, `Display 2 — DELL U2723D (2560×1440)`. |

**Logic dropdown:**
- Nếu chỉ có 1 màn hình → `IsEnabled = false`, hiển thị mờ.
- Nếu có ≥ 2 màn hình → enabled, người dùng chọn màn hình muốn đặt wallpaper.
- Thay đổi dropdown khi video đang phát → di chuyển `WallpaperWindow` sang màn hình mới ngay, không cần Save.

---

### 5.4 Section 3 — Phát video

**Label section:** `PHÁT VIDEO`

| Control | Mô tả |
|---|---|
| Label `Trạng thái` | Text cố định bên trái |
| Toggle switch | On/Off. Khi **On**: video phát, màu xanh. Khi **Off**: video dừng, wallpaper tĩnh Windows hiện lại. |
| Status text | Dòng nhỏ phía dưới toggle. Khi on: `[tên file] đang chạy` kèm dot xanh. Khi off: `Video đã tạm dừng` kèm dot xám. |

**Lưu ý UX:** Status text là feedback quan trọng vì `WallpaperWindow` không hiển thị trước mắt người dùng — đây là cách duy nhất người dùng biết video có đang chạy không.

---

### 5.5 Section 4 — Hệ thống

**Label section:** `HỆ THỐNG`

| Control | Mô tả |
|---|---|
| Checkbox `Khởi động cùng Windows` | Khi check: ghi registry autostart. Khi uncheck: xóa registry key. Thay đổi có hiệu lực ngay, không cần Save. |

---

### 5.6 Footer — Lưu & trạng thái

| Control | Mô tả |
|---|---|
| Status nhỏ bên trái | Hiển thị "Đã lưu" (xanh) khi config đã được ghi, hoặc "Chưa lưu" (xám) khi có thay đổi chưa lưu. |
| Nút `Lưu cài đặt` | Ghi `config.json`. Sau khi nhấn: nút đổi thành "Đã lưu ✓" trong ~1.5 giây rồi reset. Áp dụng thay đổi (màn hình, file) ngay lập tức. |

---

## 6. Các vấn đề kỹ thuật cần lưu ý

### 6.1 Gắn window vào WorkerW layer

Đây là phần phức tạp nhất. Windows có một process `explorer.exe` render desktop với 2 layer:
- `Progman` (Program Manager) — chứa icons
- `WorkerW` — nằm phía sau icons

Cần dùng `SendMessageTimeout` với message `0x052C` để tạo `WorkerW`, sau đó `SetParent(hwnd, workerW)` để gắn `WallpaperWindow` vào đúng layer. Nếu làm sai, video sẽ hiện đè lên icon hoặc bị các cửa sổ khác che hoàn toàn.

### 6.2 DPI Awareness

Khai báo `<dpiAware>true/PM</dpiAware>` trong `app.manifest` hoặc set `ProcessDPIAwareness` để tọa độ window không bị scale sai trên màn hình HiDPI / 4K với Windows scaling 125–150%.

### 6.3 Loop video không bị flash

`MediaEnded` → `Position = TimeSpan.Zero` → `Play()` có thể gây flash trắng/đen ngắn ở điểm loop. Dùng `MediaTimeline` với `RepeatBehavior = RepeatBehavior.Forever` thay vì xử lý event thủ công để loop mượt hơn.

### 6.4 Hiệu năng

`MediaElement` dùng hardware decode mặc định nên CPU thấp với video HD/2K. Có thể set `RenderOptions.SetBitmapScalingMode(mediaElement, BitmapScalingMode.LowQuality)` nếu muốn ưu tiên performance hơn chất lượng scale.

### 6.5 Khi explorer.exe restart

Nếu người dùng restart `explorer.exe`, handle `WorkerW` sẽ bị invalidate. Cần lắng nghe `SystemEvents` hoặc hook `WM_SETTINGCHANGE` để detect và re-attach `WallpaperWindow`.

### 6.6 Config path không tồn tại

Khi lần đầu chạy, thư mục `%AppData%\VideoWallpaper\` chưa tồn tại → cần `Directory.CreateDirectory()` trước khi ghi file.

---

## 7. Cấu trúc project gợi ý

```
VideoWallpaper/
├── App.xaml
├── App.xaml.cs              # Entry point, đọc config, xử lý --minimized arg
├── MainWindow.xaml          # SettingsWindow (UI chính)
├── MainWindow.xaml.cs
├── WallpaperWindow.xaml     # Cửa sổ video fullscreen (ẩn khỏi taskbar)
├── WallpaperWindow.xaml.cs
├── Services/
│   ├── ConfigService.cs     # Đọc/ghi config.json
│   ├── MonitorService.cs    # Lấy danh sách màn hình
│   ├── WallpaperService.cs  # Gắn window vào WorkerW layer
│   └── AutostartService.cs  # Registry autostart
├── Models/
│   └── AppConfig.cs         # Model config JSON
└── app.manifest             # DPI awareness declaration
```

---

## 8. Checklist triển khai

- [ ] Tạo project `dotnet new wpf -n VideoWallpaper`
- [ ] Implement `AppConfig` model + `ConfigService`
- [ ] Implement `MonitorService` (lấy danh sách màn hình)
- [ ] Implement `WallpaperWindow` với `MediaElement` fullscreen
- [ ] Implement `WallpaperService` (SetParent vào WorkerW)
- [ ] Implement `AutostartService` (registry)
- [ ] Build `SettingsWindow` XAML theo layout 4 section
- [ ] Wire logic: browse file, dropdown màn hình, toggle, save
- [ ] Xử lý `--minimized` argument
- [ ] Setup `NotifyIcon` (system tray)
- [ ] Xử lý `Closing` event (ẩn thay vì đóng)
- [ ] Test loop video (MediaTimeline RepeatBehavior)
- [ ] Test DPI awareness trên màn HiDPI
- [ ] Test explorer.exe restart recovery
