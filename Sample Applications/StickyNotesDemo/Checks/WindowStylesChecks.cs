using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using StickyNotesDemo;

internal static partial class Checks
{
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr handle);

    private static bool Above(IntPtr upper, IntPtr lower)
    {
        for (var previous = GetWindow(lower, 3); previous != IntPtr.Zero; previous = GetWindow(previous, 3)) // GW_HWNDPREV
            if (previous == upper) return true;
        return false;
    }

    private static void ShowOrdinaryWindow()
    {
        var ordinary = new Window { Left = 60, Top = 60, Width = 550, Height = 500, ShowActivated = false };
        ordinary.Show();
        Console.WriteLine(new WindowInteropHelper(ordinary).Handle.ToInt64());
        Console.Out.Flush();
        // EOF also ends this helper if its parent exits during a failing check.
        var app = Application.Current;
        _ = Task.Run(() => { Console.ReadLine(); app.Dispatcher.BeginInvoke(new Action(app.Shutdown)); });
        app.Run();
    }

    private static void TestPinnedWindowOrder(string desktopName, Action settle)
    {
        using var other = Child("ordinary-window", desktopName);
        var note = new Window2(new NoteData { Left = 120, Top = 160, Width = 220, Markdown = "置顶" });
        static void Order(IntPtr handle, IntPtr after)
        {
            if (!SetWindowPos(handle, after, 0, 0, 0, 0, 0x213)) // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER
                throw new Win32Exception();
        }
        try
        {
            var line = other.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            var cover = new IntPtr(long.Parse(line));
            note.Open(activate: false);
            settle();
            var handle = new WindowInteropHelper(note).Handle;
            var bounds = DesktopIntegration.GetNoteDisplay(handle).Bounds;
            var middle = new DesktopIntegration.Point { X = (bounds.Left + bounds.Right) / 2, Y = (bounds.Top + bounds.Bottom) / 2 };
            Order(cover, 0);
            Assert(IsWindowVisible(cover) && (GetWindowLongW(cover, -20) & 8) == 0 && Above(handle, cover) && WindowFromPoint(middle) == handle,
                "an overlapping ordinary window from another process stays below a restored note");
            ShowWindow(cover, 3); // SW_MAXIMIZE, on the isolated desktop.
            settle();
            Assert(IsZoomed(cover) && Above(handle, cover) && WindowFromPoint(middle) == handle,
                "maximizing the ordinary app leaves the note visible above it");
            var foreground = GetForegroundWindow();
            var active = GetActiveWindow();

            Order(handle, cover); // Placing a pin behind a non-topmost HWND removes its native topmost flag.
            Assert(note.Topmost && (GetWindowLongW(handle, -20) & 8) == 0 && Above(cover, handle) && WindowFromPoint(middle) == cover,
                "the ordinary app covers a natively demoted note even though WPF still reports Topmost");
            settle();
            Assert((GetWindowLongW(handle, -20) & 8) != 0 && Above(handle, cover) && WindowFromPoint(middle) == handle,
                "the visible paper recovers its actual position above the ordinary app without a click");

            note.SetMousePassthrough(true);
            Order(handle, -2);
            settle();
            Assert((GetWindowLongW(handle, -20) & 0x08000028) == 0x08000028 && Above(handle, cover) && WindowFromPoint(middle) == cover,
                "repair during mouse passthrough restores the pin and keeps input going to the ordinary app");
            note.SetMousePassthrough(false);
            Assert(WindowFromPoint(middle) == handle, "ending passthrough makes the still-pinned paper clickable");

            Order(handle, -2);
            note.Close();
            settle();
            Assert(!IsWindowVisible(handle) && !note.Data.IsOpen && WindowFromPoint(middle) == cover,
                "a pending repair leaves an intentionally closed note hidden");
            note.Open(activate: false);
            settle();
            Assert((GetWindowLongW(handle, -20) & 8) != 0 && WindowFromPoint(middle) == handle &&
                   DesktopIntegration.GetNoteDisplay(handle).Bounds.Equals(bounds) &&
                   GetForegroundWindow() == foreground && GetActiveWindow() == active,
                "reopening restores the pin at its saved position without activating the note");

            note.WindowState = WindowState.Minimized;
            Order(handle, -2);
            settle();
            Assert(note.WindowState == WindowState.Minimized && (GetWindowLongW(handle, -20) & 8) == 0,
                "a minimized note waits until it is restored to repair its pin");
            ShowWindow(handle, 4); // SW_SHOWNOACTIVATE, as for a native restore.
            settle();
            Assert(note.WindowState == WindowState.Normal && Above(handle, cover) && (GetWindowLongW(handle, -20) & 8) != 0 &&
                   WindowFromPoint(middle) == handle,
                "restoring a minimized note recovers its native topmost position without reopening it from the library");
        }
        finally
        {
            note.DisposeWindow();
            other.StandardInput.Close();
            if (!other.WaitForExit(10000) || other.ExitCode != 0) throw new Exception("The ordinary-window helper failed to exit normally.");
        }
    }

    private static void TestNoteWindowStyles()
    {
        var foreground = GetForegroundWindow();
        var note = new Window2(new NoteData { Markdown = "**仍可编辑**", Left = 120, Top = 160, Height = 110 });
        var normalWindow = new Window();
        try
        {
            // Create real HWNDs without Show(), activation, tray icons or global hotkeys.
            var handle = new WindowInteropHelper(note).EnsureHandle();
            var style = GetWindowLongW(handle, -20);
            Assert((style & 0x80) != 0 && (style & 0x40000) == 0,
                "note HWND is a tool window without the app-window flag before its first show");
            Assert((style & 0x80000) != 0 && (style & 0x8) != 0 && note.Topmost,
                "tool-window configuration preserves layered transparency and topmost state");
            Assert((style & 0x8000000) == 0 && note.Focusable && Find<RichTextBox>((DependencyObject)note.Content).Focusable,
                "tool notes retain normal activation and a focusable editor");
            Assert(note.Left == 120 && note.Top == 160 && note.Width == 320 && note.Height == 110,
                "configuring the hidden native window preserves the stored geometry until opening fits its content");

            note.Hide();
            Assert(new WindowInteropHelper(note).EnsureHandle() == handle && GetWindowLongW(handle, -20) == style,
                "hiding a note retains its tool-window classification for reopening");
            var normalHandle = new WindowInteropHelper(normalWindow).EnsureHandle();
            var normalStyle = GetWindowLongW(normalHandle, -20);
            Assert((normalStyle & 0x80) == 0 && (normalStyle & 0x40000) != 0,
                "ordinary WPF windows keep their application-window classification");
            Assert(!IsWindowVisible(handle) && !IsWindowVisible(normalHandle) && GetForegroundWindow() == foreground,
                "native window checks show no windows and leave desktop focus unchanged");
        }
        finally
        {
            note.DisposeWindow();
            normalWindow.Close();
        }
    }
}
