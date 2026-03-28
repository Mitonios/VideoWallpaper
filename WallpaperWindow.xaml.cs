using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VideoWallpaper.Services;

namespace VideoWallpaper;

public partial class WallpaperWindow : Window
{
    private string? _currentFilePath;
    private int _renderFrameCount;
    private int _lastCheckedFrameCount;

    public bool IsPlaybackActive { get; private set; }

    public WallpaperWindow()
    {
        InitializeComponent();
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
        CompositionTarget.Rendering += OnRendering;
        DebugLogger.Log("WallpaperWindow created.");
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (IsPlaybackActive)
            _renderFrameCount++;
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

        _currentFilePath = filePath;
        _renderFrameCount = 0;
        _lastCheckedFrameCount = 0;

        var uri = new Uri(filePath);
        DebugLogger.Log($"  URI: {uri}");

        VideoPlayer.Source = uri;
        VideoPlayer.Play();
        IsPlaybackActive = true;
        DebugLogger.Log("  VideoPlayer.Play() called.");
    }

    public void StopVideo()
    {
        if (IsPlaybackActive)
            DebugLogger.Log("StopVideo called.");

        VideoPlayer.Stop();
        VideoPlayer.Close();
        VideoPlayer.Source = null;
        IsPlaybackActive = false;
        _currentFilePath = null;
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

    /// <summary>
    /// Detect rendering freeze via CompositionTarget.Rendering frame count.
    /// Nếu frame count không tăng giữa 2 tick (10s) → WPF rendering pipeline đã chết → restart video.
    /// </summary>
    public void CheckPlaybackHealth()
    {
        if (!IsPlaybackActive || _currentFilePath == null)
            return;

        var frames = _renderFrameCount;
        var delta = frames - _lastCheckedFrameCount;
        var pos = VideoPlayer.Position;

        DebugLogger.Log($"[Heartbeat] Pos={pos:mm\\:ss\\.f}, frames={frames} (Δ{delta})");

        if (_lastCheckedFrameCount > 0 && delta == 0)
        {
            DebugLogger.Log("[Heartbeat] Rendering frozen! Δframes=0 — restarting video.");
            RestartVideo();
        }

        _lastCheckedFrameCount = frames;
    }

    private void RestartVideo()
    {
        if (_currentFilePath == null) return;

        var savedPos = VideoPlayer.Position;
        var filePath = _currentFilePath;

        VideoPlayer.Stop();
        VideoPlayer.Close();
        VideoPlayer.Source = null;

        VideoPlayer.Source = new Uri(filePath);
        VideoPlayer.Play();
        _pendingSeek = savedPos;
        DebugLogger.Log($"  RestartVideo: will seek to {savedPos:mm\\:ss\\.f} after MediaOpened.");
    }

    private TimeSpan? _pendingSeek;

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log($"[OK] MediaOpened! NaturalVideoWidth={VideoPlayer.NaturalVideoWidth}, NaturalVideoHeight={VideoPlayer.NaturalVideoHeight}");

        if (_pendingSeek.HasValue)
        {
            DebugLogger.Log($"  Seeking to saved position: {_pendingSeek.Value:mm\\:ss\\.f}");
            VideoPlayer.Position = _pendingSeek.Value;
            _pendingSeek = null;
        }
    }

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        DebugLogger.Log($"[ERROR] MediaFailed! {e.ErrorException?.GetType().Name}: {e.ErrorException?.Message}");
        DebugLogger.Log($"  Source: {VideoPlayer.Source}");

        MediaLoadFailed?.Invoke(this, e.ErrorException?.Message ?? "Unknown error");
    }

    public event Action<object, string>? MediaLoadFailed;

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        DebugLogger.Log("[Loop] MediaEnded — seeking to start.");
        VideoPlayer.Position = TimeSpan.Zero;
        VideoPlayer.Play();
    }
}
