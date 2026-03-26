using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VideoWallpaper.Services;

namespace VideoWallpaper;

public partial class WallpaperWindow : Window
{
    private Storyboard? _storyboard;

    public WallpaperWindow()
    {
        InitializeComponent();
        // Giảm priority render để không tranh CPU với foreground apps
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        DebugLogger.Log("WallpaperWindow created.");
    }

    public void PlayVideo(string filePath)
    {
        DebugLogger.Log($"PlayVideo called: {filePath}");
        DebugLogger.Log($"  Window visible={IsVisible}, pos={Left},{Top} size={Width}x{Height}");

        StopVideo();

        if (!System.IO.File.Exists(filePath))
        {
            DebugLogger.Log($"[ERROR] File not found: {filePath}");
            return;
        }

        var uri = new Uri(filePath);
        DebugLogger.Log($"  URI: {uri}");

        var timeline = new MediaTimeline(uri)
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        _storyboard = new Storyboard();
        _storyboard.Children.Add(timeline);
        Storyboard.SetTarget(timeline, VideoPlayer);

        DebugLogger.Log("  Starting Storyboard...");
        _storyboard.Begin();
        DebugLogger.Log("  Storyboard.Begin() called.");
    }

    public void StopVideo()
    {
        if (_storyboard != null)
        {
            DebugLogger.Log("StopVideo: stopping storyboard.");
            _storyboard.Stop();
            _storyboard = null;
        }
        VideoPlayer.Close();
    }

    public void PositionOnMonitor(Rectangle bounds)
    {
        DebugLogger.Log($"PositionOnMonitor: physical bounds={bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");

        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        DebugLogger.Log($"  WPF window set to Left={Left}, Top={Top}, Width={Width}, Height={Height}");
    }

    public IntPtr GetHandle()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        DebugLogger.Log($"GetHandle => 0x{handle:X8}");
        return handle;
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log($"[OK] MediaOpened! NaturalVideoWidth={VideoPlayer.NaturalVideoWidth}, NaturalVideoHeight={VideoPlayer.NaturalVideoHeight}");
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DebugLogger.Log($"[ERROR] MediaFailed! {e.ErrorException?.GetType().Name}: {e.ErrorException?.Message}");
        DebugLogger.Log($"  Source: {VideoPlayer.Source}");

        // Báo lên MainWindow để hiển thị lỗi cho user
        MediaLoadFailed?.Invoke(this, e.ErrorException?.Message ?? "Unknown error");
    }

    public event Action<object, string>? MediaLoadFailed;

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        // Với Storyboard RepeatBehavior.Forever thì không nên xảy ra
        DebugLogger.Log("MediaEnded fired (unexpected with RepeatBehavior.Forever).");
    }
}
