# Video Wallpaper

Ứng dụng desktop Windows cho phép đặt file video làm hình nền động — video chạy phía sau các icon desktop và tất cả cửa sổ, giống hệt wallpaper tĩnh mặc định của Windows.

A Windows desktop application that sets any video file as an animated wallpaper — playing behind desktop icons and all windows, just like a native static wallpaper.

## Tính năng

- Phát video MP4, MKV, AVI, MOV làm hình nền desktop
- Video nằm đúng layer wallpaper (phía sau icons, phía trước wallpaper tĩnh)
- Hỗ trợ nhiều màn hình — chọn monitor hiển thị
- Loop video mượt, không flash ở điểm lặp (MediaTimeline)
- Chạy nền dưới dạng system tray utility, không chiếm taskbar
- Khởi động cùng Windows (autostart registry)
- DPI-aware — hoạt động đúng trên màn hình HiDPI/4K
- Tự phục hồi khi explorer.exe restart

## Yêu cầu hệ thống

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Cài đặt

### Tải về

Tải bản build mới nhất từ [Releases](../../releases).

### Build từ source

```bash
git clone https://github.com/your-username/VideoWallpaper.git
cd VideoWallpaper
dotnet build
dotnet run
```

#### Publish bản portable

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Sử dụng

1. Chạy ứng dụng — cửa sổ Settings sẽ hiện ra
2. Nhấn **Browse…** để chọn file video
3. Chọn màn hình hiển thị (nếu có nhiều monitor)
4. Bật **toggle** để phát video làm wallpaper
5. Nhấn **Lưu cài đặt** để lưu cấu hình
6. Đóng cửa sổ Settings — app tiếp tục chạy ở system tray

### Tray icon

- **Click trái** → mở lại cửa sổ Settings
- **Click phải** → menu: Mở cài đặt / Thoát

### Khởi động cùng Windows

Bật checkbox "Khởi động cùng Windows" trong phần Hệ thống. App sẽ tự chạy khi Windows boot và phát video ngay mà không hiện UI.

## Cách hoạt động

App tạo một cửa sổ WPF chứa `MediaElement` phát video fullscreen, sau đó dùng Windows API (`SetParent`) để gắn cửa sổ này vào **WorkerW layer** — layer nằm phía sau desktop icons nhưng phía trên wallpaper tĩnh gốc. Đây là cách mà Windows quản lý các layer trên desktop thông qua `explorer.exe`.

## Cấu hình

File config được lưu tại:

```
%AppData%\VideoWallpaper\config.json
```

```json
{
  "videoPath": "C:\\Users\\user\\Videos\\nature_loop.mp4",
  "monitorDevice": "\\\\.\\DISPLAY1",
  "isPlaying": true,
  "autostart": true
}
```

## Stack công nghệ

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ | C# (.NET 8) |
| UI framework | WPF |
| Video rendering | MediaElement (DirectShow/Media Foundation, DXVA2 hardware decode) |
| Config | JSON (`System.Text.Json`) |

## License

MIT
