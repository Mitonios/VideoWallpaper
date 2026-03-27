using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoWallpaper.Services;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace VideoWallpaper;

public partial class MainWindow : Window
{
    private readonly ConfigService _configService;
    private readonly MonitorService _monitorService;
    private readonly WallpaperService _wallpaperService;
    private readonly AutostartService _autostartService;
    private readonly WallpaperWindow _wallpaperWindow;

    private Models.AppConfig _config;
    private List<MonitorInfo> _monitors = new();
    private bool _isInitializing = true;

    public MainWindow(
        ConfigService configService,
        MonitorService monitorService,
        WallpaperService wallpaperService,
        AutostartService autostartService,
        WallpaperWindow wallpaperWindow,
        Models.AppConfig config)
    {
        InitializeComponent();

        _configService = configService;
        _monitorService = monitorService;
        _wallpaperService = wallpaperService;
        _autostartService = autostartService;
        _wallpaperWindow = wallpaperWindow;
        _config = config;

        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version;
        var buildTime = asm.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "BuildTime")?.Value ?? "";
        Title = $"Video Wallpaper v{ver?.Major}.{ver?.Minor}.{ver?.Build} ({buildTime})";

        DebugLogger.Log($"MainWindow created. Config: VideoPath={config.VideoPath}, IsPlaying={config.IsPlaying}, MonitorDevice={config.MonitorDevice}");
        DebugLogger.Log($"Log file: {DebugLogger.LogFilePath}");

        LoadUI();
        _isInitializing = false;
    }

    private void LoadUI()
    {
        // Video file display
        if (!string.IsNullOrEmpty(_config.VideoPath) && File.Exists(_config.VideoPath))
        {
            FileNameDisplay.Text = Path.GetFileName(_config.VideoPath);
        }

        // Monitor dropdown
        _monitors = _monitorService.GetMonitors();
        MonitorDropdown.Items.Clear();
        foreach (var monitor in _monitors)
        {
            DebugLogger.Log($"Monitor: {monitor.DeviceName} | {monitor.DisplayLabel} | Bounds={monitor.Bounds}");
            MonitorDropdown.Items.Add(monitor.DisplayLabel);
        }

        if (_monitors.Count <= 1)
            MonitorDropdown.IsEnabled = false;

        // Select saved monitor or default to first
        var selectedIndex = _monitors.FindIndex(m => m.DeviceName == _config.MonitorDevice);
        MonitorDropdown.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        // Play toggle
        PlayToggle.IsChecked = _config.IsPlaying;
        UpdateStatusDisplay();

        // Autostart
        AutostartCheckbox.IsChecked = _autostartService.IsEnabled();

        // Save status
        UpdateSaveStatus(true);

        // Show log path in UI
        DebugLogPath.Text = $"Debug log: {DebugLogger.LogFilePath}";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video files (*.mp4;*.avi;*.mov;*.mkv)|*.mp4;*.avi;*.mov;*.mkv|MP4 - khuyên dùng (*.mp4)|*.mp4",
            Title = "Chọn file video"
        };

        if (dialog.ShowDialog() == true)
        {
            _config.VideoPath = dialog.FileName;
            FileNameDisplay.Text = Path.GetFileName(dialog.FileName);
            UpdateSaveStatus(false);
            DebugLogger.Log($"Video selected: {dialog.FileName}");

            // Reload video immediately if playing
            if (_config.IsPlaying)
                StartPlayback();
        }
    }

    private void MonitorDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || MonitorDropdown.SelectedIndex < 0) return;

        var monitor = _monitors[MonitorDropdown.SelectedIndex];
        _config.MonitorDevice = monitor.DeviceName;
        UpdateSaveStatus(false);
        DebugLogger.Log($"Monitor changed: {monitor.DeviceName} | Bounds={monitor.Bounds}");

        // Move wallpaper immediately if playing
        if (_config.IsPlaying)
        {
            var handle = _wallpaperWindow.GetHandle();
            _wallpaperService.PositionWindow(handle, monitor.Bounds);
        }
    }

    private void PlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _config.IsPlaying = PlayToggle.IsChecked == true;
        UpdateStatusDisplay();
        UpdateSaveStatus(false);
        DebugLogger.Log($"PlayToggle changed: IsPlaying={_config.IsPlaying}");

        if (_config.IsPlaying)
            StartPlayback();
        else
            StopPlayback();
    }

    private void AutostartCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        if (AutostartCheckbox.IsChecked == true)
            _autostartService.Enable();
        else
            _autostartService.Disable();

        _config.Autostart = AutostartCheckbox.IsChecked == true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _configService.Save(_config);
        UpdateSaveStatus(true);
        DebugLogger.Log($"Config saved. VideoPath={_config.VideoPath}, IsPlaying={_config.IsPlaying}, Monitor={_config.MonitorDevice}");

        SaveButton.Content = "Đã lưu \u2713";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            SaveButton.Content = "Lưu cài đặt";
            timer.Stop();
        };
        timer.Start();
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_config.VideoPath) || !File.Exists(_config.VideoPath))
        {
            System.Windows.MessageBox.Show("Vui lòng chọn file video trước.", "Chưa có file", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        // Kiểm tra ffmpeg có trong PATH không
        if (!IsFfmpegAvailable())
        {
            System.Windows.MessageBox.Show(
                "Không tìm thấy ffmpeg.\n\nCài đặt: https://ffmpeg.org/download.html\nSau đó thêm vào PATH và khởi động lại app.",
                "Thiếu ffmpeg", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var inputPath = _config.VideoPath;
        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + "_optimized.mp4");

        if (File.Exists(outputPath))
        {
            var overwrite = System.Windows.MessageBox.Show(
                $"File đã tồn tại:\n{Path.GetFileName(outputPath)}\n\nGhi đè?",
                "Tồn tại", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (overwrite != System.Windows.MessageBoxResult.Yes) return;
            File.Delete(outputPath);
        }

        SetOptimizeUI(isRunning: true, status: "Đang tối ưu hóa...");
        DebugLogger.Log($"OptimizeVideo: {inputPath} → {outputPath}");

        var args = $"-i \"{inputPath}\" -c:v libx264 -preset fast -crf 23 " +
                   $"-vf \"scale='min(iw,1920)':'min(ih,1080)',scale=trunc(iw/2)*2:trunc(ih/2)*2\" " +
                   $"-an -movflags +faststart -y \"{outputPath}\"";

        var (success, errorOutput) = await RunFfmpegAsync(args);

        if (success && File.Exists(outputPath))
        {
            var inputSize = new FileInfo(inputPath).Length / 1024.0 / 1024.0;
            var outputSize = new FileInfo(outputPath).Length / 1024.0 / 1024.0;
            DebugLogger.Log($"OptimizeVideo OK: {inputSize:F1}MB → {outputSize:F1}MB");

            var useNew = System.Windows.MessageBox.Show(
                $"Tối ưu xong!\n\n" +
                $"Trước: {inputSize:F1} MB  →  Sau: {outputSize:F1} MB\n\n" +
                $"Dùng file đã tối ưu ngay?",
                "Hoàn thành", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information);

            if (useNew == System.Windows.MessageBoxResult.Yes)
            {
                _config.VideoPath = outputPath;
                FileNameDisplay.Text = Path.GetFileName(outputPath);
                UpdateSaveStatus(false);
                if (_config.IsPlaying) StartPlayback();
            }
        }
        else
        {
            DebugLogger.Log($"OptimizeVideo FAILED: {errorOutput}");
            System.Windows.MessageBox.Show(
                $"ffmpeg thất bại:\n\n{errorOutput?.Split('\n').LastOrDefault(l => l.Length > 0)}",
                "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        SetOptimizeUI(isRunning: false, status: "");
    }

    private static bool IsFfmpegAvailable()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<(bool success, string? error)> RunFfmpegAsync(string args)
    {
        var psi = new ProcessStartInfo("ffmpeg", args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode == 0, stderr);
    }

    private void SetOptimizeUI(bool isRunning, string status)
    {
        OptimizeButton.IsEnabled = !isRunning;
        OptimizeProgressPanel.Visibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        OptimizeHintText.Visibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        OptimizeStatusText.Text = status;
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DebugLogger.LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"Cannot open log file: {ex.Message}");
        }
    }

    public void StartPlayback()
    {
        DebugLogger.Log("=== StartPlayback BEGIN ===");
        DebugLogger.Log($"  VideoPath={_config.VideoPath}");
        DebugLogger.Log($"  MonitorDevice={_config.MonitorDevice}");

        if (string.IsNullOrEmpty(_config.VideoPath) || !File.Exists(_config.VideoPath))
        {
            DebugLogger.Log("[ERROR] Video path invalid or file not found, aborting StartPlayback.");
            return;
        }

        var monitorIndex = _monitors.FindIndex(m => m.DeviceName == _config.MonitorDevice);
        if (monitorIndex < 0) monitorIndex = 0;
        var monitor = _monitors[monitorIndex];

        DebugLogger.Log($"  Using monitor [{monitorIndex}]: {monitor.DeviceName} | Bounds={monitor.Bounds}");

        _wallpaperWindow.Show();
        DebugLogger.Log($"  WallpaperWindow visible={_wallpaperWindow.IsVisible}");

        var handle = _wallpaperWindow.GetHandle();
        DebugLogger.Log($"  WallpaperWindow HWND=0x{handle:X8}");

        _wallpaperService.Attach(handle);

        // Sau SetParent, dùng Win32 SetWindowPos với physical pixels để tránh DPI scaling bug
        _wallpaperService.PositionWindow(handle, monitor.Bounds);

        _wallpaperWindow.PlayVideo(_config.VideoPath);

        _config.IsPlaying = true;
        UpdateStatusDisplay();
        DebugLogger.Log("=== StartPlayback END ===");
    }

    public void StopPlayback()
    {
        DebugLogger.Log("StopPlayback called.");
        _wallpaperWindow.StopVideo();
        _wallpaperWindow.Hide();
        _config.IsPlaying = false;
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        if (PlayToggle.IsChecked == true && !string.IsNullOrEmpty(_config.VideoPath))
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            StatusText.Text = $"{Path.GetFileName(_config.VideoPath)} đang chạy";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            StatusText.Text = "Video đã tạm dừng";
        }
    }

    private void UpdateSaveStatus(bool saved)
    {
        if (saved)
        {
            SaveStatusText.Text = "Đã lưu";
            SaveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        }
        else
        {
            SaveStatusText.Text = "Chưa lưu";
            SaveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
