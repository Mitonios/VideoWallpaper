using System.IO;
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
