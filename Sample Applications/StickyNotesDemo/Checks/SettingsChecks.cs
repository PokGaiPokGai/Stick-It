using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestSettings(string temp)
    {
        var directory = Path.Combine(temp, "settings");
        var store = new NoteStore(directory);
        var settings = store.LoadSettings();
        Assert(settings.RandomNewNoteColor, "new installations enable random note colors by default");
        var random = new Random(42);
        var colors = Enumerable.Range(0, 32).Select(_ => settings.CreateNote(random).Color).ToArray();
        Assert(colors.All(color => NoteData.Colors.Contains(color)) && colors.Distinct().Count() > 1,
               "new random notes use the same palette as manual recoloring");
        store.Save([new NoteData { Color = "#E2F1FF", Markdown = "保留已有便签的颜色" }]);
        var savedNotes = File.ReadAllText(Path.Combine(directory, "notes.json"));
        var library = new MainWindow(store);
        var toggle = (MenuItem)library.FindName("RandomNoteColor");
        Assert(toggle.IsChecked, "the settings menu reflects the enabled random-color default");
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var reopened = new MainWindow(new NoteStore(directory));
        Assert(!((MenuItem)reopened.FindName("RandomNoteColor")).IsChecked &&
               !store.LoadSettings().RandomNewNoteColor && store.LoadSettings().CreateNote(random).Color == NoteData.DefaultColor,
               "disabling random colors persists across reopening and new notes return to yellow");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert(store.LoadSettings().RandomNewNoteColor && File.ReadAllText(Path.Combine(directory, "notes.json")) == savedNotes,
               "re-enabling random colors is saved without modifying existing notes");

        Directory.CreateDirectory(Path.Combine(directory, "settings.json.tmp"));
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var status = (TextBlock)library.FindName("StatusText");
        Assert(toggle.IsChecked && store.LoadSettings().RandomNewNoteColor && status.Visibility == Visibility.Visible &&
               status.Text.StartsWith("设置未能保存：", StringComparison.Ordinal),
               "a settings write failure restores the switch and reports the error");
        File.WriteAllText(Path.Combine(directory, "settings.json"), "null");
        try { store.LoadSettings(); throw new Exception("invalid settings accepted"); }
        catch (InvalidDataException) { Console.WriteLine("PASS invalid settings are rejected rather than replaced with defaults"); }
        library.PrepareForShutdown();
        library.Close();
        reopened.PrepareForShutdown();
        reopened.Close();
    }
}
