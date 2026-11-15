using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestLongNoteMovement(string temp)
    {
        const string searchTerm = "长文末尾索引";
        var longMarkdown = "## 长便签\n\n" + string.Join("\n\n", Enumerable.Range(1, 96).Select(index =>
            $"### 小节 {index}\n\n**重点** {new string('文', 400)}")) + "\n\n" + searchTerm;
        var store = new NoteStore(Path.Combine(temp, "note-movement"));
        var main = new MainWindow(store);
        var data = new NoteData { Markdown = longMarkdown, Left = 100, Top = 100 };
        var note = (Window2)Invoke(main, "AddNote", data);
        Invoke(main, "AddNote", new NoteData { Markdown = "第二张便签" });
        Invoke(main, "RefreshList");
        var list = (ItemsControl)main.FindName("NoteList");
        var source = list.ItemsSource;
        var surface = (FrameworkElement)main.Content;
        surface.Measure(new Size(478, 720));
        surface.Arrange(new Rect(0, 0, 478, 720));
        surface.UpdateLayout();

        var locationChanged = typeof(Window).GetMethod("OnLocationChanged", BindingFlags.Instance | BindingFlags.NonPublic)
            .CreateDelegate<Action<EventArgs>>(note);
        const int moves = 60;
        var refreshes = 0;
        var start = Stopwatch.GetTimestamp();
        var beforeAllocations = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < moves; i++)
        {
            note.Left = 120 + i;
            note.Top = 130 + i;
            locationChanged(EventArgs.Empty);
            if (!ReferenceEquals(source, list.ItemsSource))
            {
                refreshes++;
                source = list.ItemsSource;
            }
            surface.UpdateLayout();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocations;
        Console.WriteLine($"Long note movement ({moves} events): {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2} ms, {allocated:N0} bytes, {refreshes} library refreshes");
        Assert(refreshes == 0, "window moves do not regenerate the note cards");
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("saveTimer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(main);
        Assert(timer.IsEnabled && data.Left == 179 && data.Top == 189, "window moves still queue saving the final position");
        timer.Stop();
        Assert((bool)Invoke(main, "Save") && store.Load().Single(saved => saved.Id == data.Id) is { Left: 179, Top: 189 } saved &&
               saved.Markdown == longMarkdown, "window movement saves the full note and final coordinates");

        var preview = list.Items.OfType<NoteCard>().Single(card => ReferenceEquals(card.Note, note));
        var previewText = new TextRange(preview.Preview.ContentStart, preview.Preview.ContentEnd).Text.TrimEnd();
        Assert(previewText.StartsWith("长便签", StringComparison.Ordinal) && previewText.Length <= 660 && previewText.EndsWith("…", StringComparison.Ordinal),
               "the library lays out a bounded preview for long notes");
        var search = (TextBox)main.FindName("SearchBox");
        search.Text = searchTerm;
        Assert(list.Items.Count == 1 && ReferenceEquals(((NoteCard)list.Items[0]).Note, note),
               "library search still matches text beyond the card preview");

        const string editedTerm = "修改后的长文末尾";
        var editor = (RichTextBox)typeof(Window2).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(note);
        var last = (Paragraph)editor.Document.Blocks.LastBlock;
        source = list.ItemsSource;
        ((Run)last.Inlines.FirstInline).Text = editedTerm;
        Assert(ReferenceEquals(source, list.ItemsSource), "editing a note does not rebuild the hidden library");
        search.Text = editedTerm;
        Assert(list.Items.Count == 1 && ReferenceEquals(((NoteCard)list.Items[0]).Note, note),
               "full-text search reflects edits after caching a long note");
        timer.Stop();

        var oneLine = new Window2(new NoteData { Markdown = new string('字', 639) + "\U0001F600" + new string('字', 500) });
        var oneLineCard = new NoteCard(oneLine);
        Assert(new TextRange(oneLineCard.Preview.ContentStart, oneLineCard.Preview.ContentEnd).Text.TrimEnd() == new string('字', 639) + "…",
               "a long one-line preview truncates without splitting an emoji");
    }
}
