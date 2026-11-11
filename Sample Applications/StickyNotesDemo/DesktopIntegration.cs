using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace StickyNotesDemo;

public static class DesktopIntegration
{
    public const int HotkeyId = 0x5349;
    public const uint HotkeyModifiers = 0x4000 | 0x1 | 0x4; // NOREPEAT + ALT + SHIFT
    public const uint HotkeyKey = 0x57;
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", SetLastError=true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    internal static System.Drawing.Size TrayIconSize()
    {
        const int smallIconWidth = 49;
        const int smallIconHeight = 50;
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        var dpi = GetDpiForWindow(taskbar);
        if (dpi == 0)
            return System.Windows.Forms.SystemInformation.SmallIconSize;

        return new System.Drawing.Size(
            GetSystemMetricsForDpi(smallIconWidth, dpi),
            GetSystemMetricsForDpi(smallIconHeight, dpi));
    }

    public static Rect CenteredBounds(Point center, Rect work, int width, int height)
    {
        var workWidth = work.Right - work.Left;
        var workHeight = work.Bottom - work.Top;
        if (width == height)
        {
            var side = Math.Min(width, Math.Min(workWidth, workHeight));
            width = height = side;
        }
        else
        {
            width = Math.Min(width, workWidth);
            height = Math.Min(height, workHeight);
        }
        var left = Math.Clamp(center.X - width / 2, work.Left, work.Right - width);
        var top = Math.Clamp(center.Y - height / 2, work.Top, work.Bottom - height);
        return new Rect { Left=left, Top=top, Right=left+width, Bottom=top+height };
    }

    public static void PlaceAtCursor(Window window)
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception();
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        Marshal.ThrowExceptionForHR(GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY));
        var bounds = CenteredBounds(point, info.Work, (int)Math.Round(window.Width*dpiX/96), (int)Math.Round(window.Height*dpiY/96));
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (!SetWindowPos(hwnd, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Right-bounds.Left, bounds.Bottom-bounds.Top, 0x10))
            throw new Win32Exception();
    }
}
