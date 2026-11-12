using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestTaskCompletion(string temp)
    {
        const string uncheckedMarkdown = "- [ ] 阅读并记录\n- [ ] 后续";
        const string checkedMarkdown = "- [x] 阅读并记录\n- [ ] 后续";
        var note = new EditorSession(uncheckedMarkdown);
        var task = note.Row(0);
        Assert(!ShowsTaskStrike(task), "an unchecked task has no completion strike");

        Invoke(note.Window, "FinishEditing");
        var checkbox = TaskCheckbox(task);
        var surface = (FrameworkElement)note.Window.Content;
        surface.UpdateLayout();
        var center = checkbox.TranslatePoint(new Point(checkbox.ActualWidth / 2, checkbox.ActualHeight / 2), surface);
        var hit = VisualTreeHelper.HitTest(surface, center);
        Assert(checkbox.DesiredSize.Width == 0 && hit != null &&
               (ReferenceEquals(hit.VisualHit, checkbox) || checkbox.IsAncestorOf(hit.VisualHit)),
               "the checkbox stays clickable in the gutter without consuming text width");
        var canUndo = note.Editor.CanUndo;
        FormatToggle(checkbox).Toggle();
        DrainFormatting();
        Assert(ShowsTaskStrike(task) && !ShowsTaskStrike(note.Row(0, 1)) && note.Markdown == checkedMarkdown,
               "checking a task strikes its label and saves only the checked marker");
        Assert(note.Editor.CanUndo == canUndo, "a completion style change does not add a text undo entry");

        var store = new NoteStore(Path.Combine(temp, "task-completion"));
        store.Save([note.Window.Data]);
        var reopened = new EditorSession(store.Load().Single().Markdown);
        Assert(ShowsTaskStrike(reopened.Row(0)) && !ShowsTaskStrike(reopened.Row(0, 1)),
               "saved checked tasks show their completion strike on reopening");
        reopened.End(reopened.Row(0));
        reopened.Key(Key.Enter);
        Assert(ShowsTaskStrike(reopened.Row(0)) && !ShowsTaskStrike(reopened.Row(0, 1)),
               "an unchecked task created after a checked task has no completion strike");

        Invoke(note.Window, "BeginEditing");
        note.Select(task, task);
        DrainFormatting();
        var strike = FindFormatButton(FormattingBar(note.Window), "删除线");
        Assert(strike.IsChecked == false, "completion styling does not activate the manual strikethrough button");
        note.Key(Key.X, FormatShortcut);
        Assert(strike.IsChecked == true && note.Markdown.StartsWith("- [x] <s>阅读并记录</s>", StringComparison.Ordinal),
               "manual strikethrough can be applied while the task is checked");
        FormatToggle(TaskCheckbox(task)).Toggle();
        DrainFormatting();
        Assert(!ShowsTaskStrike(task) && strike.IsChecked == true &&
               note.Markdown.StartsWith("- [ ] <s>阅读并记录</s>", StringComparison.Ordinal),
               "unchecking removes only the completion strike");
        note.Key(Key.X, FormatShortcut);
        Assert(note.Markdown == uncheckedMarkdown, "manual strikethrough remains separately removable");

        note = new EditorSession("- [x] 已完成");
        note.End(note.Row(0));
        note.Key(Key.D9, FormatShortcut);
        Assert(note.Markdown == "已完成" && !ShowsTaskStrike(note.Body()),
               "converting a checked task to body text clears the completion strike");
        note.Key(Key.Z, ModifierKeys.Control);
        Assert(note.Markdown == "- [x] 已完成" && ShowsTaskStrike(note.Row(0)),
               "undo restores the checked task and its completion strike");
        note.Key(Key.Z, FormatShortcut);
        Assert(note.Markdown == "已完成" && !ShowsTaskStrike(note.Body()),
               "redo removes the completion strike with the checkbox");
    }

    private static bool ShowsTaskStrike(Paragraph paragraph) =>
        paragraph.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough);

    private static CheckBox TaskCheckbox(Paragraph paragraph) =>
        (CheckBox)paragraph.Inlines.OfType<InlineUIContainer>().Single().Child;
}
