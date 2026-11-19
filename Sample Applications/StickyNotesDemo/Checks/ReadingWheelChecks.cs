using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestReadingWheel()
    {
        var session = new EditorSession(string.Join("\n", Enumerable.Range(1, 80).Select(i => $"- 内容 {i}")));
        var note = session.Window;
        var surface = (FrameworkElement)note.Content;
        Invoke(note, "FinishEditing");
        surface.UpdateLayout();
        var scroller = Find<ScrollViewer>(session.Editor);
        var scrollbar = (ScrollBar)scroller.Template.FindName("PART_VerticalScrollBar", scroller);
        var initialMarkdown = note.Data.Markdown;
        var foreground = GetForegroundWindow();
        var offset = scroller.VerticalOffset;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        note.RaiseEvent(wheel);
        surface.UpdateLayout();
        Assert(wheel.Handled && (SystemParameters.WheelScrollLines == 0 || scroller.VerticalOffset > offset) &&
               session.Editor.IsReadOnly && scrollbar.Opacity == 0 && !scrollbar.IsHitTestVisible &&
               !((NoteChrome)note.Content).BarsVisible && note.Data.Markdown == initialMarkdown,
            "wheel over a reading note scrolls content without editing, showing controls or changing saved text");

        var handle = new WindowInteropHelper(note).EnsureHandle();
        GetNoteBounds(handle, out var bounds);
        var point = new IntPtr(((long)(ushort)(bounds.Top + 40) << 16) | (ushort)(bounds.Left + 40));
        var hook = typeof(Window2).GetMethod("NoteWindowMessage", BindingFlags.NonPublic | BindingFlags.Instance);
        bool NativeWheel(int delta, IntPtr position)
        {
            object[] args = [handle, 0x020A, new IntPtr(delta << 16), position, false];
            hook.Invoke(note, args);
            surface.UpdateLayout();
            return (bool)args[4];
        }
        offset = scroller.VerticalOffset;
        Assert(NativeWheel(-120, point) && (SystemParameters.WheelScrollLines == 0 || scroller.VerticalOffset > offset),
            "an inactive note handles its native hover-wheel message without requiring keyboard focus");
        offset = scroller.VerticalOffset;
        Assert(NativeWheel(120, point) && (SystemParameters.WheelScrollLines == 0 || scroller.VerticalOffset < offset),
            "hover-wheel scrolling supports both directions");
        var outside = new IntPtr(((long)(ushort)(bounds.Bottom + 10) << 16) | (ushort)(bounds.Right + 10));
        offset = scroller.VerticalOffset;
        Assert(!NativeWheel(-120, outside) && scroller.VerticalOffset == offset,
            "a wheel message whose pointer is outside the note is not captured");
        var clearMargin = new IntPtr(((long)(ushort)(bounds.Top + 2) << 16) | (ushort)(bounds.Left + 40));
        Assert(!NativeWheel(-120, clearMargin) && scroller.VerticalOffset == offset,
            "reading ignores wheel input in the hidden toolbar space inside the native window");
        note.SetMousePassthrough(true);
        Assert(!NativeWheel(-120, point) && scroller.VerticalOffset == offset,
            "game mouse passthrough leaves wheel input to the game");
        note.SetMousePassthrough(false);
        Assert(scrollbar.Opacity == 0 && !IsWindowVisible(handle) && GetForegroundWindow() == foreground,
            "reading-wheel validation keeps the scrollbar and native window hidden and preserves desktop focus");
        note.DisposeWindow();
    }
}
