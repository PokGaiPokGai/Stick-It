using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestSettings(string temp, string output)
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
        var search = (TextBox)library.FindName("SearchBox");
        search.Text = "保留";
        var list = (ItemsControl)library.FindName("NoteList");
        var originalItems = list.ItemsSource;
        var foreground = GetForegroundWindow();
        var windowCount = Application.Current.Windows.Count;
        var settingsButton = (Button)library.FindName("SettingsButton");
        var settingsPage = (ScrollViewer)library.FindName("SettingsPage");
        var notesPage = (Grid)library.FindName("NotesPage");
        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(settingsPage.Visibility == Visibility.Visible && notesPage.Visibility == Visibility.Collapsed &&
               ((TextBlock)library.FindName("PageTitle")).Text == "设置" && settingsButton.ContextMenu == null &&
               Application.Current.Windows.Count == windowCount,
               "the gear replaces the list with settings inside the same window without opening a menu");
        Render((FrameworkElement)library.Content, 478, 720, Path.Combine(output, "settings-page.png"));
        Render((FrameworkElement)library.Content, 320, 380, Path.Combine(output, "settings-page-narrow.png"));
        Assert(settingsPage.ScrollableHeight > 0 && settingsPage.ScrollableWidth == 0,
               "settings wrap at minimum window width and remain vertically scrollable");
        settingsPage.ScrollToBottom();
        library.UpdateLayout();
        var exit = (Button)library.FindName("ExitButton");
        var exitPosition = exit.TranslatePoint(new Point(), settingsPage);
        Assert(exitPosition.Y >= 0 && exitPosition.Y + exit.ActualHeight <= settingsPage.ViewportHeight,
               "save and exit actions remain reachable in the shortest settings window");
        Render((FrameworkElement)library.Content, 320, 380, Path.Combine(output, "settings-page-narrow-bottom.png"));
        ((Button)library.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(notesPage.Visibility == Visibility.Visible && settingsPage.Visibility == Visibility.Collapsed &&
               search.Text == "保留" && ReferenceEquals(list.ItemsSource, originalItems),
               "returning from settings keeps the existing search and note list");
        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var escape = new KeyEventArgs(offlineKeyboard, new OfflineInputSource { RootVisual = library }, Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        library.RaiseEvent(escape);
        Assert(escape.Handled && settingsPage.Visibility == Visibility.Collapsed &&
               !library.IsVisible && GetForegroundWindow() == foreground,
               "Escape returns to notes through an in-process event without desktop input or activation");
        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        using var trayToggle = new System.Windows.Forms.ToolStripMenuItem { Checked = true };
        typeof(MainWindow).GetField("trayMouseMode", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(library, trayToggle);
        var mouseToggle = (CheckBox)library.FindName("AutoMouseMode");
        mouseToggle.IsChecked = false;
        mouseToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(!trayToggle.Checked, "the in-window game switch also updates the tray toggle without creating a tray icon");

        var toggle = (CheckBox)library.FindName("RandomNoteColor");
        Assert(toggle.IsChecked == true, "the settings page reflects the enabled random-color default");
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var reopened = new MainWindow(new NoteStore(directory));
        Assert(((CheckBox)reopened.FindName("RandomNoteColor")).IsChecked == false &&
               !store.LoadSettings().RandomNewNoteColor && store.LoadSettings().CreateNote(random).Color == NoteData.DefaultColor,
               "disabling random colors persists across reopening and new notes return to yellow");
        toggle.IsChecked = true;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert(store.LoadSettings().RandomNewNoteColor && File.ReadAllText(Path.Combine(directory, "notes.json")) == savedNotes,
               "re-enabling random colors is saved without modifying existing notes");

        Directory.CreateDirectory(Path.Combine(directory, "settings.json.tmp"));
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var status = (TextBlock)library.FindName("StatusText");
        Assert(toggle.IsChecked == true && store.LoadSettings().RandomNewNoteColor && status.Visibility == Visibility.Visible &&
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
