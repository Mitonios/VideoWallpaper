using System.Drawing;
using System.Runtime.InteropServices;

namespace VideoWallpaper.Services;

public class WallpaperService
{
    private IntPtr _workerW = IntPtr.Zero;
    private RECT _workerWRect;

    #region P/Invoke

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const uint SMTO_NORMAL = 0x0000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    #endregion

    public static uint WM_TASKBAR_CREATED { get; } = RegisterWindowMessage("TaskbarCreated");

    public IntPtr GetWorkerW()
    {
        var progman = FindWindow("Progman", null);
        DebugLogger.Log($"Progman handle: 0x{progman:X8} (valid={IsWindow(progman)})");

        if (progman == IntPtr.Zero)
        {
            DebugLogger.Log("[ERROR] Progman window not found!");
            return IntPtr.Zero;
        }

        var sendResult = SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero,
            SMTO_NORMAL, 1000, out _);
        DebugLogger.Log($"SendMessageTimeout(0x052C) result: 0x{sendResult:X8}");

        IntPtr workerW = IntPtr.Zero;
        int windowCount = 0;

        EnumWindows((hwnd, _) =>
        {
            windowCount++;
            var shellView = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                var cls = new System.Text.StringBuilder(256);
                GetClassName(hwnd, cls, 256);
                DebugLogger.Log($"Found SHELLDLL_DefView under hwnd=0x{hwnd:X8} (class={cls})");

                var candidate = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                DebugLogger.Log($"Candidate WorkerW: 0x{candidate:X8}");

                if (candidate != IntPtr.Zero && GetWindowRect(candidate, out var r))
                {
                    _workerWRect = r;
                    DebugLogger.Log($"WorkerW rect: {r.Left},{r.Top} - {r.Right},{r.Bottom}");
                    workerW = candidate;
                }
            }
            return true;
        }, IntPtr.Zero);

        DebugLogger.Log($"EnumWindows scanned {windowCount} windows. Final WorkerW: 0x{workerW:X8}");

        if (workerW == IntPtr.Zero)
            DebugLogger.Log("[ERROR] WorkerW not found!");

        _workerW = workerW;
        return workerW;
    }

    public void Attach(IntPtr wpfHandle)
    {
        DebugLogger.Log($"Attach: wpfHandle=0x{wpfHandle:X8}");

        if (_workerW == IntPtr.Zero)
            GetWorkerW();

        if (_workerW != IntPtr.Zero)
        {
            var prev = SetParent(wpfHandle, _workerW);
            var err = Marshal.GetLastWin32Error();
            DebugLogger.Log($"SetParent => prevParent=0x{prev:X8}, LastError={err}");
        }
        else
        {
            DebugLogger.Log("[ERROR] Cannot attach: WorkerW is Zero.");
        }
    }

    /// <summary>
    /// Sau khi SetParent vào WorkerW, dùng SetWindowPos với physical pixel để
    /// bypass WPF coordinate system (bị lệch do DPI scaling).
    /// Tọa độ screenBounds là screen coordinates (từ Screen.Bounds).
    /// </summary>
    public void PositionWindow(IntPtr hwnd, Rectangle screenBounds)
    {
        if (_workerW == IntPtr.Zero)
        {
            DebugLogger.Log("[WARN] PositionWindow called but not attached yet, skipping.");
            return;
        }

        // Child window coordinates = screen coords - WorkerW origin (physical pixels)
        int childX = screenBounds.X - _workerWRect.Left;
        int childY = screenBounds.Y - _workerWRect.Top;
        int w = screenBounds.Width;
        int h = screenBounds.Height;

        DebugLogger.Log($"PositionWindow: screen={screenBounds.X},{screenBounds.Y} {w}x{h} " +
                        $"→ child={childX},{childY} (workerW origin={_workerWRect.Left},{_workerWRect.Top})");

        var ok = SetWindowPos(hwnd, IntPtr.Zero, childX, childY, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
        var err = Marshal.GetLastWin32Error();
        DebugLogger.Log($"SetWindowPos => ok={ok}, LastError={err}");
    }

    public void Detach(IntPtr wpfHandle)
    {
        DebugLogger.Log($"Detach wpfHandle=0x{wpfHandle:X8}");
        SetParent(wpfHandle, IntPtr.Zero);
    }

    public void ReAttach(IntPtr wpfHandle)
    {
        DebugLogger.Log("ReAttach (explorer restarted)");
        _workerW = IntPtr.Zero;
        GetWorkerW();
        if (_workerW != IntPtr.Zero)
            SetParent(wpfHandle, _workerW);
    }
}
