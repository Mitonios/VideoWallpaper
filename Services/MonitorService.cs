using System.Drawing;
using WinForms = System.Windows.Forms;

namespace VideoWallpaper.Services;

public class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required Rectangle Bounds { get; init; }
    public required bool IsPrimary { get; init; }
    public required string DisplayLabel { get; init; }
}

public class MonitorService
{
    public List<MonitorInfo> GetMonitors()
    {
        var screens = WinForms.Screen.AllScreens;
        var monitors = new List<MonitorInfo>();

        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            var label = screen.Primary
                ? $"Display {i + 1} — Primary ({screen.Bounds.Width}x{screen.Bounds.Height})"
                : $"Display {i + 1} — {screen.DeviceName.TrimStart('\\')} ({screen.Bounds.Width}x{screen.Bounds.Height})";

            monitors.Add(new MonitorInfo
            {
                DeviceName = screen.DeviceName,
                Bounds = screen.Bounds,
                IsPrimary = screen.Primary,
                DisplayLabel = label
            });
        }

        return monitors;
    }
}
