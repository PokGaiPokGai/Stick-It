using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    // Window styles are 32-bit values on both x86 and x64.
    [DllImport("user32.dll", ExactSpelling=true, SetLastError=true)]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll", ExactSpelling=true, SetLastError=true)]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);

    internal static void ConfigureNoteWindow(IntPtr hwnd)
        => UpdateExtendedStyle(hwnd, 0x80, 0x40000); // TOOLWINDOW, APPWINDOW

    internal static void SetNoteMousePassthrough(IntPtr hwnd, bool enabled)
    {
        const int passthrough = 0x20 | 0x08000000; // TRANSPARENT (layered window) | NOACTIVATE
        UpdateExtendedStyle(hwnd, enabled ? passthrough : 0, enabled ? 0 : passthrough);
    }

    private static void UpdateExtendedStyle(IntPtr hwnd, int add, int remove)
    {
        const int extendedStyle = -20;
        var style = GetWindowLongW(hwnd, extendedStyle);
        if (style == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();

        if (SetWindowLongW(hwnd, extendedStyle, (style | add) & ~remove) == 0 &&
            Marshal.GetLastWin32Error() != 0) throw new Win32Exception();

        const uint refreshFrame = 0x20 | 0x1 | 0x2 | 0x4 | 0x10; // FRAMECHANGED, NOSIZE, NOMOVE, NOZORDER, NOACTIVATE
        if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, refreshFrame)) throw new Win32Exception();
    }

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

    internal static void PlaceAtCursor(Window2 window)
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception();
        PlaceAtPoint(window, point);
    }

    internal static void PlaceAtPoint(Window2 window, Point point)
    {
        var monitor = MonitorFromPoint(point, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception();
        Marshal.ThrowExceptionForHR(GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY));
        var width = Math.Min((int)Math.Round(Math.Max(window.Width, window.MinWidth) * dpiX / 96), info.Work.Right - info.Work.Left);
        var display = new NoteDisplay(default, info.Work, dpiY / 96.0);
        var paperHeight = window.MeasurePaperHeight(width * 96.0 / dpiX, display.AvailablePaperHeight);
        var bounds = CenteredBounds(point, info.Work, width, display.WindowHeight(paperHeight));
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (!SetWindowPos(hwnd, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Right-bounds.Left, bounds.Bottom-bounds.Top, 0x10))
            throw new Win32Exception();
    }

    internal static Rect VisibleBounds(Rect bounds, Rect work)
    {
        if (bounds.Left < work.Right && bounds.Right > work.Left && bounds.Top < work.Bottom && bounds.Bottom > work.Top)
            return bounds;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        return CenteredBounds(new Point { X = bounds.Left + width / 2, Y = bounds.Top + height / 2 }, work, width, height);
    }

    internal static System.Windows.Point PointInWindow(IntPtr window, IntPtr position)
    {
        if (!GetWindowRect(window, out var bounds)) throw new Win32Exception();
        var x = unchecked((short)position.ToInt64());
        var y = unchecked((short)(position.ToInt64() >> 16));
        var scale = GetDpiForWindow(window) / 96.0;
        return new System.Windows.Point((x - bounds.Left) / scale, (y - bounds.Top) / scale);
    }

    internal readonly record struct NoteDisplay(Rect Bounds, Rect Work, double Scale)
    {
        private const double MinimumPaperHeight = 160;
        internal double Width => (Bounds.Right - Bounds.Left) / Scale;
        internal int BarPixels => Math.Min((int)Math.Round(NoteChrome.BarHeight * Scale), (Work.Bottom - Work.Top - 1) / 2);
        internal double AvailablePaperHeight => (Work.Bottom - Work.Top - 2 * BarPixels) / Scale;

        internal int WindowHeight(double paperHeight) => (int)Math.Min(Work.Bottom - Work.Top,
            Math.Ceiling(Math.Max(MinimumPaperHeight, paperHeight) * Scale) + 2 * BarPixels);

        internal Rect FitWindow(double desiredHeight, int preferredTop)
        {
            var height = WindowHeight(desiredHeight);
            var bar = BarPixels;
            var top = Math.Clamp(preferredTop, Work.Top + bar, Work.Bottom - height + bar) - bar;
            return new Rect { Left = Bounds.Left, Right = Bounds.Right, Top = top, Bottom = top + height };
        }
    }

    internal static NoteDisplay GetNoteDisplay(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var bounds)) throw new Win32Exception();
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromRect(ref bounds, 2), ref info)) throw new Win32Exception();
        return new NoteDisplay(bounds, info.Work, GetDpiForWindow(handle) / 96.0);
    }

    internal static void SetNoteBounds(IntPtr handle, Rect bounds)
    {
        if (!SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top, 0x4 | 0x10)) throw new Win32Exception(); // NOZORDER | NOACTIVATE
    }

}
