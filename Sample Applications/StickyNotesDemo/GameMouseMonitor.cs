using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace StickyNotesDemo;

internal sealed class GameMouseMonitor : IDisposable
{
    private const uint CursorShow = 0x8002, CursorHide = 0x8003, ForegroundChanged = 3;
    private readonly MousePassthroughPolicy policy = new();
    private readonly Action<bool> apply;
    private readonly Action<Win32Exception> failed;
    private readonly DispatcherTimer timer;
    private readonly WinEventCallback callback;
    private IntPtr cursorHook, foregroundHook;
    internal bool Passthrough { get; private set; }

    internal GameMouseMonitor(Action<bool> apply, Action<Win32Exception> failed)
    {
        this.apply = apply;
        this.failed = failed;
        callback = OnWinEvent;
        // ClipCursor has no matching WinEvent; sample confinement as well.
        timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => Update();
    }

    internal void Start()
    {
        try
        {
            cursorHook = SetWinEventHook(CursorShow, CursorHide, IntPtr.Zero, callback, 0, 0, 2); // OUTOFCONTEXT | SKIPOWNPROCESS
            if (cursorHook == IntPtr.Zero) throw new Win32Exception("无法注册光标显隐监听。");
            foregroundHook = SetWinEventHook(ForegroundChanged, ForegroundChanged, IntPtr.Zero, callback, 0, 0, 0);
            if (foregroundHook == IntPtr.Zero) throw new Win32Exception("无法注册前台窗口监听。");
            timer.Start();
            Update();
        }
        catch (Win32Exception error) { Fail(error); }
    }

    internal void Stop()
    {
        timer.Stop();
        if (cursorHook != IntPtr.Zero) UnhookWinEvent(cursorHook);
        if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
        cursorHook = foregroundHook = IntPtr.Zero;
        policy.Reset();
        Publish(false);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint time)
    {
        if (!timer.IsEnabled) return;
        if (eventType == ForegroundChanged) Update();
        else if (objectId == -9) Update(eventType, eventThread); // OBJID_CURSOR
    }

    private void Update(uint eventType = 0, uint eventThread = 0)
    {
        try
        {
            var state = ReadSnapshot();
            // Ignore events from background threads, including old queued events
            // after switching applications. Samples still cover confinement changes.
            var signal = eventThread != 0 && eventThread == state.ForegroundThread
                ? eventType == CursorHide ? CursorSignal.Hidden : CursorSignal.Shown
                : CursorSignal.Sample;
            Observe(state, signal);
        }
        catch (Win32Exception error) { Fail(error); }
    }

    internal void Observe(PointerSnapshot state, CursorSignal signal = CursorSignal.Sample)
        => Publish(policy.Update(state, signal));

    private void Publish(bool value)
    {
        if (Passthrough == value) return;
        Passthrough = value;
        apply(value);
    }

    private void Fail(Win32Exception error)
    {
        Stop();
        failed(error);
    }

    public void Dispose() => Stop();

    internal static PointerSnapshot ReadSnapshot()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return default;
        var thread = GetWindowThreadProcessId(foreground, out var process);
        if (process == Environment.ProcessId)
            return new(foreground, thread, true, default, default, false, true);

        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor)) throw new Win32Exception();
        if (!GetClipCursor(out var clip)) throw new Win32Exception();
        if (!GetWindowRect(foreground, out var bounds))
        {
            // The foreground window can be destroyed between the two queries.
            if (foreground != GetForegroundWindow()) return default;
            throw new Win32Exception();
        }
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(foreground, 2), ref monitor)) throw new Win32Exception();
        var desktop = new DesktopIntegration.Rect
        {
            Left = GetSystemMetrics(76), Top = GetSystemMetrics(77),
        };
        desktop.Right = desktop.Left + GetSystemMetrics(78);
        desktop.Bottom = desktop.Top + GetSystemMetrics(79);
        var visibility = (cursor.Flags & 2) != 0 ? CursorVisibility.Suppressed :
            (cursor.Flags & 1) != 0 ? CursorVisibility.Visible : CursorVisibility.Hidden;
        var underPointer = WindowFromPoint(cursor.Position);
        GetWindowThreadProcessId(underPointer, out var pointerProcess);
        if (foreground != GetForegroundWindow()) return default;
        return new(foreground, thread, false, visibility, ClassifyConstraint(clip, desktop, bounds),
            Contains(bounds, monitor.Monitor), pointerProcess == Environment.ProcessId);
    }

    internal static CursorConstraint ClassifyConstraint(DesktopIntegration.Rect clip, DesktopIntegration.Rect desktop, DesktopIntegration.Rect foreground)
    {
        if (clip.Equals(desktop) || !Contains(foreground, clip)) return CursorConstraint.Free;
        return clip.Right - clip.Left <= 2 && clip.Bottom - clip.Top <= 2
            ? CursorConstraint.Locked : CursorConstraint.Confined;
    }

    private static bool Contains(DesktopIntegration.Rect outer, DesktopIntegration.Rect inner) =>
        outer.Left <= inner.Left + 2 && outer.Top <= inner.Top + 2 &&
        outer.Right >= inner.Right - 2 && outer.Bottom >= inner.Bottom - 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size; public uint Flags; public IntPtr Cursor; public DesktopIntegration.Point Position; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public DesktopIntegration.Rect Monitor, Work; public uint Flags; }
    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClipCursor(out DesktopIntegration.Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out DesktopIntegration.Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(DesktopIntegration.Point point);
}
