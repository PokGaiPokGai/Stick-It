using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        var settingsPath = Path.Combine(directory, "settings.json");
        const string legacySettings = """{"RandomNewNoteColor":true}""";
        File.WriteAllText(settingsPath, legacySettings);
        var library = new MainWindow(store);
        var font = (Slider)library.FindName("FontSizeSlider");
        var valueLabel = (TextBlock)library.FindName("FontSizeValue");
        Assert(font.Value == 100 && File.ReadAllText(settingsPath) == legacySettings,
            "opening existing settings keeps the original font size without writing a preference");
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
        var note = (Window2)Invoke(library, "AddNote", new NoteData { Markdown = "# 标题\n\n保留正文\n\n~~~\n代码\n~~~\n\n- [x] 待办" });
        Invoke(library, "RefreshList");
        var card = list.Items.Cast<NoteCard>().Single();
        var editor = Find<RichTextBox>((DependencyObject)note.Content);
        var sheet = (NoteChrome)note.Content;
        var document = editor.Document;
        var paragraphs = document.Blocks.OfType<Paragraph>().ToArray();
        var task = (Paragraph)document.Blocks.OfType<List>().Single().ListItems.FirstListItem.Blocks.FirstBlock;
        var checkbox = TaskCheckbox(task);
        var noteText = note.Data.Markdown;
        var modified = note.Data.Modified;
        Invoke(note, "BeginEditing");
        editor.Selection.Select(paragraphs[1].ContentStart, paragraphs[1].ContentEnd);
        double[] GlyphHeights() => paragraphs.Append(task).Select(row => editor.TransformToAncestor(sheet)
            .TransformBounds(NoteLists.TextStart(row).GetCharacterRect(LogicalDirection.Forward)).Height).ToArray();
        Render(sheet, 320, 480, Path.Combine(output, "font-default.png"));
        var originalHeights = GlyphHeights();
        var originalBoxWidth = checkbox.TransformToAncestor(sheet).TransformBounds(new Rect(checkbox.RenderSize)).Width;
        int edits = 0;
        editor.TextChanged += (_, _) => edits++;
        settingsPage.ScrollToTop();
        Render((FrameworkElement)library.Content, 320, 380, Path.Combine(output, "font-slider.png"));
        var track = (Track)font.Template.FindName("PART_Track", font);
        var travel = track.ActualWidth - track.Thumb.ActualWidth;
        var range = (IRangeValueProvider)new SliderAutomationPeer(font).GetPattern(PatternInterface.RangeValue);
        Assert(range.Minimum == 75 && range.Maximum == 200 && range.SmallChange == 5 && font.TickFrequency == 5 &&
               font.IsSnapToTickEnabled && font.IsMoveToPointEnabled && travel > 100 && valueLabel.Text == "100%",
               "the font slider exposes the existing range, five-percent ticks, track clicks and a readable percentage");
        var originalTrack = track.TranslatePoint(new Point(), settingsPage);
        void BeginDrag() => track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        void MoveThumb(double delta) => track.Thumb.RaiseEvent(new DragDeltaEventArgs(delta, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        void EndDrag(bool canceled = false) => track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });
        BeginDrag();
        MoveThumb(travel / 5);
        MoveThumb(travel / 5);
        WaitForFontPreview();
        Assert(font.Value == 150 && valueLabel.Text == "150%" && ((ScaleTransform)library.Resources["DocumentTextScale"]).ScaleX == 1.5 &&
               track.TranslatePoint(new Point(), settingsPage) == originalTrack && File.ReadAllText(settingsPath) == legacySettings,
               "dragging previews the latest size with a steady track and leaves saved preferences untouched");
        EndDrag(true);
        Assert(font.Value == 100 && valueLabel.Text == "100%" && ((ScaleTransform)library.Resources["DocumentTextScale"]).ScaleX == 1 &&
               File.ReadAllText(settingsPath) == legacySettings, "canceling a drag restores the original text and percentage");
        var key = new KeyEventArgs(offlineKeyboard, new OfflineInputSource { RootVisual = library }, Environment.TickCount, Key.Right)
            { RoutedEvent = Keyboard.KeyDownEvent };
        font.RaiseEvent(key);
        Assert(font.Value == 105 && store.LoadSettings().FontSizePercent == 105, "the Right arrow changes and saves one step");
        range.SetValue(117);
        Assert(font.Value == 115 && store.LoadSettings().FontSizePercent == 115, "an accessibility value snaps to a supported tick before saving");
        BeginDrag();
        MoveThumb(travel);
        EndDrag();
        Assert(font.Value == 200 && valueLabel.Text == "200%" && store.LoadSettings().FontSizePercent == 200,
               "releasing the thumb saves the last position even before a pending preview is drawn");
        Render(sheet, 320, 480, Path.Combine(output, "font-200.png"));
        Assert(originalHeights.All(h => h > 0) && GlyphHeights().Zip(originalHeights, (large, original) => Math.Abs(large - 2 * original) < .1).All(ok => ok) &&
               originalBoxWidth > 0 && Math.Abs(checkbox.TransformToAncestor(sheet).TransformBounds(new Rect(checkbox.RenderSize)).Width - 2 * originalBoxWidth) < .1,
            "the rendered heading, body, code, task text and checkbox double together");
        Assert(edits == 0 && ReferenceEquals(editor.Document, document) && editor.Selection.Text == "保留正文" &&
               note.Data.Markdown == noteText && note.Data.Modified == modified && checkbox.IsChecked == true,
            "changing the display size preserves the selection, Markdown, modification time and completed task");
        settingsPage.ScrollToTop();
        Render((FrameworkElement)library.Content, 320, 380, Path.Combine(output, "settings-font-200.png"));
        var sliderBounds = font.TransformToAncestor(settingsPage).TransformBounds(new Rect(font.RenderSize));
        Assert(((TextBlock)library.FindName("PageTitle")).FontSize == 44 && settingsPage.ScrollableWidth == 0 &&
               sliderBounds.Left >= 0 && sliderBounds.Right <= settingsPage.ActualWidth &&
               sliderBounds.Top >= 0 && sliderBounds.Bottom <= settingsPage.ActualHeight && settingsPage.ScrollableHeight > 0,
            "the largest interface text and size control fit the minimum-width settings page");
        ((Button)library.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Render((FrameworkElement)library.Content, 320, 550, Path.Combine(output, "library-font-200.png"));
        var viewer = Find<FlowDocumentScrollViewer>(list);
        Assert(ReferenceEquals(viewer.Document, card.Preview) && viewer.LayoutTransform.Value.M22 == 2 && search.FontSize == 26,
            "existing library previews and search text adopt the selected size without recreating the note card");
        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        toggle.IsChecked = false;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var reopened = new MainWindow(new NoteStore(directory));
        Assert(((CheckBox)reopened.FindName("RandomNoteColor")).IsChecked == false && ((Slider)reopened.FindName("FontSizeSlider")).Value == 200 &&
               !store.LoadSettings().RandomNewNoteColor && store.LoadSettings().CreateNote(random).Color == NoteData.DefaultColor,
               "the font preference and the random-color switch both persist across reopening");
        var restored = ((Window2[])Invoke(reopened, "LoadNotesToRestore")).Single();
        var fresh = (Window2)Invoke(reopened, "AddNote", new NoteData());
        foreach (var other in new[] { restored, fresh })
        {
            var body = (FrameworkElement)VisualTreeHelper.GetParent(Find<RichTextBox>((DependencyObject)other.Content));
            Assert(body.LayoutTransform.Value.M22 == 2, "restored and newly created notes inherit the saved text size");
        }
        font.Value = 75;
        Assert(!store.LoadSettings().RandomNewNoteColor && store.LoadSettings().FontSizePercent == 75,
            "choosing a smaller size preserves the existing color preference");
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
        BeginDrag();
        font.Value = 150;
        WaitForFontPreview();
        EndDrag();
        Assert(font.Value == 75 && valueLabel.Text == "75%" && store.LoadSettings().FontSizePercent == 75 &&
               ((TextBlock)library.FindName("PageTitle")).FontSize == 16.5 && ((ScaleTransform)library.Resources["DocumentTextScale"]).ScaleX == .75,
               "a failed save rolls the slider, percentage and preview back to the saved size");
        File.WriteAllText(settingsPath, """{"FontSizePercent":0}""");
        try { store.LoadSettings(); throw new Exception("invalid font size accepted"); }
        catch (InvalidDataException) { Console.WriteLine("PASS an unsupported saved size follows the settings error path"); }
        File.WriteAllText(Path.Combine(directory, "settings.json"), "null");
        try { store.LoadSettings(); throw new Exception("invalid settings accepted"); }
        catch (InvalidDataException) { Console.WriteLine("PASS invalid settings are rejected rather than replaced with defaults"); }
        library.PrepareForShutdown();
        note.DisposeWindow();
        library.Close();
        reopened.PrepareForShutdown();
        restored.DisposeWindow();
        fresh.DisposeWindow();
        reopened.Close();
    }

    private static void WaitForFontPreview()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
