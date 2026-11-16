using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestGameMousePolicy()
    {
        var policy = new MousePassthroughPolicy();
        var view = new PointerSnapshot(new IntPtr(101), 10, false,
            CursorVisibility.Hidden, CursorConstraint.Confined, false, false);
        var menu = view with { Visibility = CursorVisibility.Visible, Constraint = CursorConstraint.Free };
        Assert(policy.Update(view), "windowed hidden/confined view enables mouse passthrough");
        Assert(!policy.Update(menu), "visible unrestricted game menu immediately restores note interaction");
        Assert(policy.Update(view) && !policy.Update(menu) && policy.Update(view),
            "repeated view/menu switches do not leave notes in the previous mode");

        var fullscreen = view with { Fullscreen = true, Constraint = CursorConstraint.Free };
        policy.Reset();
        Assert(policy.Update(fullscreen), "fullscreen hidden cursor works when confinement equals the entire desktop");
        Assert(!policy.Update(fullscreen with { Visibility = CursorVisibility.Visible }),
            "fullscreen visible cursor restores interaction without requiring a window-mode change");
        Assert(!policy.Update(view with { Visibility = CursorVisibility.Visible }),
            "visible cursor confined to a game window remains usable for clicking notes");
        Assert(!policy.Update(view with { Constraint = CursorConstraint.Free }),
            "ordinary windowed text-entry cursor hiding does not enable game passthrough");
        Assert(!policy.Update(view with { Visibility = CursorVisibility.Suppressed }),
            "touch and pen cursor suppression does not enable game passthrough");
        Assert(policy.Update(view with { Constraint = CursorConstraint.Locked, Visibility = CursorVisibility.Visible, OverOwnWindow = true }),
            "point-locked cursor stays protected even when an overlapping note supplied a cursor");

        Assert(!policy.Update(view with { OwnForeground = true }),
            "actively editing Stick It restores interaction even if the previous game left clipping behind");
        Assert(policy.Update(view) && !policy.Update(menu with { Foreground = new IntPtr(202) }),
            "switching to another application releases the old game's passthrough state");
        Assert(policy.Update(view) && !policy.Update(default),
            "a missing foreground window releases passthrough");

        policy.Reset();
        var overNote = view with { Visibility = CursorVisibility.Visible, OverOwnWindow = true };
        Assert(!policy.Update(overNote) && policy.Update(overNote, CursorSignal.Hidden),
            "foreground hide event overrides a cursor supplied by an overlapping note");
        Assert(policy.Update(overNote) && policy.Update(overNote),
            "a stale note cursor cannot undo passthrough before the game updates its cursor");
        Assert(policy.Update(view, CursorSignal.Shown),
            "a queued show event does not override a newer hidden cursor sample");
        Assert(!policy.Update(overNote, CursorSignal.Shown),
            "foreground show event restores interaction while the pointer is over a note");
        Assert(!policy.Update(menu, CursorSignal.Hidden),
            "a queued hide event does not override a newer visible cursor outside Stick It");

        policy.Reset();
        Assert(policy.Update(view with { Fullscreen = true, Constraint = CursorConstraint.Locked }) &&
               !policy.Update(fullscreen with { OverOwnWindow = true }) && !policy.Update(fullscreen),
            "releasing a locked software cursor restores interaction without oscillating on hidden samples");
        Assert(policy.Update(fullscreen, CursorSignal.Hidden),
            "a new foreground hide event starts protection after a previous release");
        policy.Reset();
        Assert(!policy.Passthrough, "disabling automatic mode clears its latched state");

        var desktop = new DesktopIntegration.Rect { Left = -1920, Top = 0, Right = 1920, Bottom = 1080 };
        var game = new DesktopIntegration.Rect { Left = -1800, Top = 120, Right = -200, Bottom = 1000 };
        Assert(GameMouseMonitor.ClassifyConstraint(desktop, desktop, game) == CursorConstraint.Free &&
               GameMouseMonitor.ClassifyConstraint(game, desktop, game) == CursorConstraint.Confined,
            "confinement detection handles a game on a negative-coordinate monitor");
        Assert(GameMouseMonitor.ClassifyConstraint(new DesktopIntegration.Rect { Left = -1000, Top = 500, Right = -999, Bottom = 501 }, desktop, game)
               == CursorConstraint.Locked,
            "single-pixel cursor lock is distinguished from a movable confined cursor");
        Assert(GameMouseMonitor.ClassifyConstraint(new DesktopIntegration.Rect { Right = 1920, Bottom = 1080 }, desktop, game)
               == CursorConstraint.Free,
            "a clip rectangle outside the foreground window is not attributed to that game");
        Assert(GameMouseMonitor.ClassifyConstraint(desktop, desktop, desktop) == CursorConstraint.Free,
            "fullscreen equal to the desktop is treated as ambiguous rather than falsely reported as clipped");
    }

    private static void TestGameMouseWindows()
    {
        const int inputFlags = 0x08000020;
        var foreground = GetForegroundWindow();
        var data = new NoteData { Markdown = "## 保留内容\n\n- [x] 保存\n- 编辑", Left = 210, Top = 240 };
        var note = new Window2(data);
        using var monitor = new GameMouseMonitor(note.SetMousePassthrough, error => throw error);
        var view = new PointerSnapshot(new IntPtr(101), 10, false,
            CursorVisibility.Hidden, CursorConstraint.Confined, false, false);
        var menu = view with { Visibility = CursorVisibility.Visible, Constraint = CursorConstraint.Free };
        try
        {
            monitor.Observe(view);
            Assert(new WindowInteropHelper(note).Handle == IntPtr.Zero && note.MousePassthrough,
                "restoring a protected note records passthrough without creating or showing a window");
            var handle = new WindowInteropHelper(note).EnsureHandle();
            var protectedStyle = GetWindowLongW(handle, -20);
            Assert((protectedStyle & inputFlags) == inputFlags && (protectedStyle & 0x80088) == 0x80088 &&
                   (protectedStyle & 0x40000) == 0,
                "protected note HWND is transparent to mouse input and nonactivating while remaining a topmost tool window");
            var editor = Find<RichTextBox>((DependencyObject)note.Content);
            var document = editor.Document;
            monitor.Observe(menu);
            Assert((GetWindowLongW(handle, -20) & inputFlags) == 0 &&
                   (GetWindowLongW(handle, -20) & ~inputFlags) == (protectedStyle & ~inputFlags),
                "restoring interaction removes only the two input flags");
            Invoke(note, "BeginEditing");
            Assert(!editor.IsReadOnly, "editing is available after passthrough ends");
            monitor.Observe(view);
            Assert(editor.IsReadOnly && ReferenceEquals(editor.Document, document),
                "entering game view ends note editing without replacing its formatted document");
            note.Hide();
            Assert((GetWindowLongW(handle, -20) & inputFlags) == inputFlags,
                "a hidden protected note retains passthrough for session restoration");
            monitor.Stop();
            Assert(!note.MousePassthrough && (GetWindowLongW(handle, -20) & inputFlags) == 0,
                "disabling the monitor immediately restores native mouse interaction");
            Assert(data.Markdown == "## 保留内容\n\n- [x] 保存\n- 编辑" && note.Left == 210 && note.Top == 240,
                "switching input modes does not modify note contents or position");
            Assert(!IsWindowVisible(handle) && GetForegroundWindow() == foreground,
                "input-mode integration checks neither show windows nor change desktop focus");
        }
        finally { note.DisposeWindow(); }
    }

    private static void TestGameMouseMonitorLifecycle()
    {
        var foreground = GetForegroundWindow();
        var errors = new List<Exception>();
        var changes = new List<bool>();
        using var monitor = new GameMouseMonitor(changes.Add, errors.Add);
        monitor.Start();
        // Pump a few read-only samples; no cursor movement, clipping, hiding,
        // foreground changes or app windows are requested by this test.
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
        stop.Start();
        Dispatcher.PushFrame(frame);
        monitor.Stop();
        monitor.Start();
        monitor.Stop();
        Assert(errors.Count == 0 && !monitor.Passthrough,
            "native cursor/foreground subscriptions and confinement sampling start and stop cleanly");
        Assert(GetForegroundWindow() == foreground,
            "read-only monitor lifecycle leaves foreground focus unchanged");
    }
}
