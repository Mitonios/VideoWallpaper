using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Drawing;
using VideoWallpaper.Services;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace VideoWallpaper;

public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    private static uint _showSettingsMsg;

    private Mutex? _mutex;
    private WinForms.NotifyIcon? _trayIcon;
    private WallpaperWindow? _wallpaperWindow;
    private MainWindow? _mainWindow;
    private WallpaperService? _wallpaperService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers — log trước khi crash
        DispatcherUnhandledException += (_, ex) =>
        {
            DebugLogger.LogError("DispatcherUnhandledException", ex.Exception);
            MessageBox.Show($"Lỗi không xử lý được:\n{ex.Exception.Message}\n\nXem log: {DebugLogger.LogFilePath}",
                "Video Wallpaper - Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception exception)
                DebugLogger.LogError("UnhandledException (AppDomain)", exception);
            else
                DebugLogger.Log($"[CRASH] UnhandledException: {ex.ExceptionObject}");
        };

        DebugLogger.Log("App startup begin.");

        _showSettingsMsg = RegisterWindowMessage("VideoWallpaper_ShowSettings");

        // Single instance check
        _mutex = new Mutex(true, "VideoWallpaper_SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Báo instance đang chạy mở cửa sổ cài đặt
            PostMessage(HWND_BROADCAST, _showSettingsMsg, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        var isMinimized = e.Args.Contains("--minimized");
        DebugLogger.Log($"isMinimized={isMinimized}");

        try
        {
            // Initialize services
            var configService = new ConfigService();
            var monitorService = new MonitorService();
            _wallpaperService = new WallpaperService();
            var autostartService = new AutostartService();

            // Load config
            var config = configService.Load();
            DebugLogger.Log($"Config loaded: VideoPath={config.VideoPath}, IsPlaying={config.IsPlaying}");

            // Create windows
            DebugLogger.Log("Creating WallpaperWindow...");
            _wallpaperWindow = new WallpaperWindow();
            _wallpaperWindow.MediaLoadFailed += (_, msg) =>
            {
                MessageBox.Show(
                    $"Không thể phát video.\n\n{msg}\n\nFile .mkv có thể cần cài thêm codec (K-Lite Codec Pack).\nĐịnh dạng được hỗ trợ tốt nhất: MP4 (H.264).",
                    "Lỗi phát video", MessageBoxButton.OK, MessageBoxImage.Warning);
            };

            DebugLogger.Log("Creating MainWindow...");
            _mainWindow = new MainWindow(
                configService, monitorService, _wallpaperService,
                autostartService, _wallpaperWindow, config);

            // Setup tray icon
            DebugLogger.Log("Setting up tray icon...");
            SetupTrayIcon();

            // Auto-play if configured
            if (config.IsPlaying && !string.IsNullOrEmpty(config.VideoPath) && File.Exists(config.VideoPath))
            {
                DebugLogger.Log("Auto-playing on startup...");
                _mainWindow.StartPlayback();
            }

            // Show settings window unless --minimized
            if (!isMinimized)
            {
                DebugLogger.Log("Showing MainWindow...");
                _mainWindow.Show();
            }

            // Setup explorer restart recovery
            SetupExplorerRestartHook();
            DebugLogger.Log("App startup complete.");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CRASH in OnStartup", ex);
            MessageBox.Show($"Lỗi khởi động:\n{ex.Message}\n\nXem log: {DebugLogger.LogFilePath}",
                "Video Wallpaper - Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "Video Wallpaper",
            Visible = true
        };

        // Load icon.ico from embedded WPF resources (pack://application:/...)
        var iconUri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
        var iconStream = Application.GetResourceStream(iconUri)?.Stream;
        if (iconStream != null)
        {
            // NotifyIcon takes ownership of the Icon object.
            _trayIcon.Icon = new Icon(iconStream);
        }

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Mở cài đặt", null, (_, _) => ShowSettings());
        contextMenu.Items.Add(new WinForms.ToolStripSeparator());
        contextMenu.Items.Add("Thoát", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = contextMenu;

        _trayIcon.Click += (_, args) =>
        {
            if (args is WinForms.MouseEventArgs mouseArgs && mouseArgs.Button == WinForms.MouseButtons.Left)
                ShowSettings();
        };
    }

    private void ShowSettings()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void ExitApp()
    {
        _mainWindow?.StopPlayback();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        Shutdown();
    }

    private void SetupExplorerRestartHook()
    {
        if (_mainWindow == null || _wallpaperWindow == null) return;

        // Listen for TaskbarCreated message (broadcast when explorer.exe restarts)
        var source = HwndSource.FromHwnd(new WindowInteropHelper(_mainWindow).EnsureHandle());
        source?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            var uMsg = (uint)msg;

            if (uMsg == WallpaperService.WM_TASKBAR_CREATED)
            {
                // Explorer restarted, re-attach wallpaper after a short delay
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (_wallpaperWindow.IsVisible && _wallpaperService != null)
                    {
                        _wallpaperService.ReAttach(_wallpaperWindow.GetHandle());
                    }
                };
                timer.Start();
            }
            else if (uMsg == _showSettingsMsg)
            {
                ShowSettings();
                handled = true;
            }

            return IntPtr.Zero;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wallpaperWindow?.StopVideo();
        _wallpaperWindow?.Close();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
