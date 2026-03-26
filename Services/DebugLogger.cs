using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace VideoWallpaper.Services;

public static class DebugLogger
{
    private static readonly string LogPath = Path.Combine(
        AppContext.BaseDirectory, "debug.log");

    private static readonly object _lock = new();

    static DebugLogger()
    {

        // Xóa log cũ khi khởi động
        try { File.WriteAllText(LogPath, $"=== VideoWallpaper Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n"); }
        catch { /* ignore */ }

        Log($"Log file: {LogPath}");
    }

    public static string LogFilePath => LogPath;

    public static void Log(string message,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{fileName}.{caller}] {message}";

        Debug.WriteLine(line);

        lock (_lock)
        {
            try { File.AppendAllText(LogPath, line + "\n"); }
            catch { /* ignore */ }
        }
    }

    public static void LogError(string message, Exception? ex = null,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        var detail = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}" : message;
        Log($"[ERROR] {detail}", caller, file);
        if (ex?.InnerException != null)
            Log($"[ERROR] Inner: {ex.InnerException.Message}", caller, file);
    }
}
