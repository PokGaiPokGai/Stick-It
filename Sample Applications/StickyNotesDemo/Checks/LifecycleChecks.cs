using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
    private static extern bool GetNoteBounds(IntPtr window, out DesktopIntegration.Rect bounds);

    private static void TestReviewLifecycle(string temp)
    {
        var directory = Path.Combine(temp, "session-save-failure");
        var store = new NoteStore(directory);
        store.Save([new NoteData { Markdown = "saved" }]);
        var library = new MainWindow(store);
        var notes = (Window2[])Invoke(library, "LoadNotesToRestore");
        notes[0].Data.Markdown = "pending edit";
        Directory.CreateDirectory(Path.Combine(directory, "notes.json.tmp"));
        Assert(!library.EndSession() && store.Load()[0].Markdown == "saved" && notes[0].Data.Markdown == "pending edit",
            "failed session saving returns failure and retains the pending note");
        Invoke(library, "OnNotesChanged");
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("saveTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(library);
        Assert(timer.IsEnabled && !(bool)typeof(Window2).GetField("disposing", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(notes[0]),
            "failed session saving leaves autosave and note lifetime available");
        timer.Stop();
        Directory.Delete(Path.Combine(directory, "notes.json.tmp"));
        Assert(library.EndSession() && store.Load()[0].Markdown == "pending edit", "a later successful session save preserves the pending content");
        library.Close();
        notes[0].DisposeWindow();

        var originalMainWindow = Application.Current.MainWindow;
        Application.Current.MainWindow = null;
        var brokenDirectory = Path.Combine(temp, "startup-settings-failure");
        Directory.CreateDirectory(brokenDirectory);
        File.WriteAllText(Path.Combine(brokenDirectory, "settings.json"), "null");
        try
        {
            try { _ = new MainWindow(new NoteStore(brokenDirectory)); throw new Exception("invalid settings accepted"); }
            catch (InvalidDataException) { }
            Assert(Application.Current.MainWindow == null && !File.Exists(Path.Combine(brokenDirectory, "notes.json")),
                "failed settings loading creates no partially initialized application window or empty note file");
        }
        finally { Application.Current.MainWindow = originalMainWindow; }

        var work = new DesktopIntegration.Rect { Left = -1920, Top = 0, Right = 0, Bottom = 1040 };
        var outside = new DesktopIntegration.Rect { Left = -5000, Top = -1000, Right = -4680, Bottom = -680 };
        var recovered = DesktopIntegration.VisibleBounds(outside, work);
        Assert(recovered.Left >= work.Left && recovered.Top >= work.Top && recovered.Right <= work.Right && recovered.Bottom <= work.Bottom,
            "offscreen notes recover into a negative-coordinate monitor work area");
        var partlyVisible = new DesktopIntegration.Rect { Left = -2000, Top = 20, Right = -1680, Bottom = 340 };
        Assert(DesktopIntegration.VisibleBounds(partlyVisible, work).Equals(partlyVisible), "visible saved positions are not moved");
        var foreground = GetForegroundWindow();
        var note = new Window2(new NoteData { Left = -20000, Top = -20000 });
        try
        {
            DesktopIntegration.EnsureVisible(note);
            var handle = new WindowInteropHelper(note).Handle;
            Assert(GetNoteBounds(handle, out var bounds) && bounds.Left != -20000 && bounds.Top != -20000,
                "native recovery relocates a saved note outside all monitors");
            DesktopIntegration.EnsureVisible(note);
            Assert(GetNoteBounds(handle, out var unchanged) && unchanged.Equals(bounds) && !IsWindowVisible(handle) && GetForegroundWindow() == foreground,
                "rechecking recovered bounds keeps the note hidden and does not activate it");
        }
        finally { note.DisposeWindow(); }
    }
}
