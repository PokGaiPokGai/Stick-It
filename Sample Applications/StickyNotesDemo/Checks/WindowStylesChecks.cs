using System;
using System.Runtime.InteropServices;
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
