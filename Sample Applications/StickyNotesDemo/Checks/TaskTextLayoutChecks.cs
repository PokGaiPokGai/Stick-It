using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestTaskTextLayout(string output)
    {
        foreach (var completed in new[] { false, true })
        foreach (var label in new[] { "待办内容", "" })
        {
            var prefix = completed ? "- [x] " : "- [ ] ";
            var note = new EditorSession(label.Length == 0 ? "" : prefix + label);
            try
            {
                if (label.Length == 0)
                {
                    note.Key(Key.D9, FormatShortcut);
                    if (completed) FormatToggle(TaskCheckbox(note.Row(0))).Toggle();
                }
                var original = note.Markdown;
                note.End(note.Row(0));
                note.Key(Key.Home);
                note.Type("新增ABC");
                var expected = prefix + "新增ABC" + label;
                Assert(note.Row(0).Inlines.FirstInline == NoteLists.TaskOf(note.Row(0)) && note.Markdown == expected,
                    $"Home then typing keeps the task marker before its label (checked={completed}, empty={label.Length == 0}, actual={note.Markdown})");
                AssertTaskTextGap(note, "typed task label stays clear of its checkbox");
                if (!completed && label.Length != 0)
                    Render((FrameworkElement)note.Window.Content, 320, 320, Path.Combine(output, "task-text-start.png"));

                note.Key(Key.Z, ModifierKeys.Control);
                Assert(note.Markdown == original, "undo at a task start restores text and completion state");
                note.Key(Key.Z, FormatShortcut);
                Assert(note.Markdown == expected, "redo at a task start preserves a valid task");
            }
            finally { note.Window.DisposeWindow(); }
        }

        TestTaskSelectionBoundary();
        TestTaskArrowNavigation();
        TestWrappedTaskLayout(output);
    }

    private static void TestTaskSelectionBoundary()
    {
        var note = new EditorSession("- [x] 原文字");
        try
        {
            // This is the insertion position WPF can choose for a gutter hit.
            note.Editor.CaretPosition = note.Row(0).ContentStart;
            note.Type("前缀");
            Assert(note.Markdown == "- [x] 前缀原文字", "typing after a leading-marker hit stays after the checkbox");
            note.End(note.Row(0));
            note.Key(Key.Home, ModifierKeys.Shift);
            Assert(note.Editor.Selection.Text == "前缀原文字", "Shift+Home selects the label without selecting its checkbox");
            note.Type("替换");
            Assert(note.Markdown == "- [x] 替换" && ShowsTaskStrike(note.Row(0)),
                "replacing a task label keeps its checked marker and completion style");

            note.Key(Key.Home);
            var data = new DataObject(DataFormats.UnicodeText, "**粘贴**");
            note.Editor.RaiseEvent(new DataObjectPastingEventArgs(data, false, DataFormats.UnicodeText));
            Assert(note.Markdown == "- [x] <strong>粘贴</strong>替换", $"inline Markdown paste at a task start keeps text after the checkbox ({note.Markdown})");
            AssertTaskTextGap(note, "pasted task text stays clear of the checkbox");

            note.Key(Key.Home);
            note.Key(Key.Back);
            Assert(note.Markdown == "<strong>粘贴</strong>替换", "Backspace at the protected label start still exits the task list");
        }
        finally { note.Window.DisposeWindow(); }

        note = new EditorSession("- [x] 第一项\n- [ ] 第二项");
        try
        {
            note.Editor.Selection.Select(note.Row(0, 1).ContentEnd, note.Row(0).ContentStart);
            note.Type("跨行替换");
            Assert(note.Markdown == "- [x] 跨行替换", "backward selection across tasks keeps one leading checkbox when replaced");
            note.Key(Key.Z, ModifierKeys.Control);
            Assert(note.Markdown == "- [x] 第一项\n- [ ] 第二项", "undo restores both task rows after selection replacement");
        }
        finally { note.Window.DisposeWindow(); }
    }

    private static void TestTaskArrowNavigation()
    {
        var note = new EditorSession("- [ ] first item\n- [ ] second item");
        try
        {
            note.Start(note.Row(0));
            note.Key(Key.Left);
            Assert(note.Editor.CaretPosition.CompareTo(NoteLists.TextStart(note.Row(0))) == 0,
                "Left at the first task stays at its editable start");
            note.Start(note.Row(0, 1));
            note.Key(Key.Left);
            Assert(note.Editor.CaretPosition.Paragraph == note.Row(0) &&
                   NoteLists.InlineText(note.Editor.CaretPosition, note.Row(0).ContentEnd) == "",
                "Left crosses from a task start to the previous row without stopping on the checkbox");
            note.Key(Key.Right);
            note.Type("X");
            Assert(note.Markdown == "- [ ] first item\n- [ ] Xsecond item", "Right enters the next task at its label start");

            note.Start(note.Row(0, 1));
            note.Key(Key.Left, ModifierKeys.Control);
            Assert(note.Editor.CaretPosition.Paragraph == note.Row(0) &&
                   NoteLists.InlineText(note.Editor.CaretPosition, note.Row(0).ContentEnd) == "item",
                "Ctrl+Left crosses a task boundary to the previous word");
            note.Start(note.Row(0, 1));
            note.Key(Key.Left, ModifierKeys.Shift);
            Assert(note.Editor.Selection.Start.Paragraph == note.Row(0) &&
                   note.Editor.Selection.End.Paragraph == note.Row(0, 1),
                "Shift+Left extends selection into the preceding task row");
            note.Start(note.Row(0, 1));
            note.Key(Key.Left, ModifierKeys.Control | ModifierKeys.Shift);
            Assert(note.Editor.Selection.Start.Paragraph == note.Row(0) &&
                   note.Editor.Selection.Text.StartsWith("item", StringComparison.Ordinal),
                "Ctrl+Shift+Left selects the previous word across a task boundary");
        }
        finally { note.Window.DisposeWindow(); }

        note = new EditorSession("- [ ] first item\n- [x] **second** item");
        try
        {
            note.End(note.Row(0, 1));
            note.Key(Key.Home);
            note.Key(Key.Left);
            Assert(note.Editor.CaretPosition.Paragraph == note.Row(0),
                "Left also crosses a task boundary when the label starts with nested formatting");
            note.End(note.Row(0, 1));
            note.Key(Key.Home);
            note.Type("前缀");
            Assert(NoteLists.InlineText(NoteLists.TextStart(note.Row(0, 1)), note.Row(0, 1).ContentEnd) == "前缀second item" &&
                   TaskCheckbox(note.Row(0, 1)).IsChecked == true,
                "typing at a formatted task start preserves its text order and checkbox");
        }
        finally { note.Window.DisposeWindow(); }
    }

    private static void TestWrappedTaskLayout(string output)
    {
        var note = new EditorSession("- [ ] 这是一段足够长的待办文字，用来检查窄便签中换行和行首编辑时的文字位置。\n- [x] 已完成的待办文字");
        try
        {
            var surface = (FrameworkElement)note.Window.Content;
            Render(surface, 200, 320, Path.Combine(output, "task-text-wrapped.png"));
            var row = note.Row(0);
            var first = NoteLists.TextStart(row).GetCharacterRect(LogicalDirection.Forward);
            var last = row.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            Assert(last.Top > first.Top, "task layout check includes actual wrapped text");
            note.End(row);
            note.Key(Key.Home);
            var atLineStart = note.Editor.CaretPosition;
            note.Type("续行");
            Assert(atLineStart.CompareTo(NoteLists.TextStart(row)) > 0 &&
                   !NoteLists.InlineText(NoteLists.TextStart(row), row.ContentEnd).StartsWith("续行", StringComparison.Ordinal),
                "Home on a wrapped continuation stays on that visual line");
            note.Key(Key.Home, ModifierKeys.Control);
            note.Type("起始");
            Assert(note.Markdown.StartsWith("- [ ] 起始", StringComparison.Ordinal), "Ctrl+Home inserts after the first task checkbox");
            Render(surface, 200, 320, Path.Combine(output, "task-text-wrapped.png"));
            AssertTaskTextGap(note, "wrapped task label stays clear of its checkbox");
        }
        finally { note.Window.DisposeWindow(); }
    }

    private static void AssertTaskTextGap(EditorSession note, string message)
    {
        ((FrameworkElement)note.Window.Content).UpdateLayout();
        var row = note.Row(0);
        var checkbox = TaskCheckbox(row);
        var box = (Border)checkbox.Template.FindName("Box", checkbox);
        var bounds = box.TransformToAncestor(note.Editor).TransformBounds(new Rect(box.RenderSize));
        var text = NoteLists.TextStart(row).GetCharacterRect(LogicalDirection.Forward);
        Assert(!text.IsEmpty && text.Left - bounds.Right >= 4 && text.Top < bounds.Bottom && text.Bottom > bounds.Top,
            message);
    }
}
