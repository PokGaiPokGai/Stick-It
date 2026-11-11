using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private const ModifierKeys FormatShortcut = ModifierKeys.Control | ModifierKeys.Shift;
    private const ModifierKeys HeadingShortcut = ModifierKeys.Control | ModifierKeys.Alt;

    private static void TestEditorBlocks(string temp, string output)
    {
        var note = new EditorSession("## 字");
        note.End(note.Body());
        note.Key(Key.Back);
        Assert(note.Body().FontSize == 14 && note.Markdown == "", "Backspace on the last heading character returns to body text");
        note.Key(Key.Z, ModifierKeys.Control);
        Assert(note.Body().FontSize == 22 && note.Markdown == "## 字",
               $"undo restores heading content and its block format together (size={note.Body().FontSize}, saved={note.Markdown}, text={note.Window.PlainText})");
        note.Key(Key.Z, FormatShortcut);
        note.Type("新文字");
        Assert(note.Markdown == "新文字" && note.Body().Inlines.FirstInline.FontSize == 14,
               "typing after redo inherits body formatting");

        note = new EditorSession("正文\n\n### 标题");
        note.Start(note.Body(1));
        note.Key(Key.Back);
        Assert(note.Markdown == "正文\n\n标题" && note.Editor.CaretPosition.Paragraph == note.Body(1),
               "Backspace at a nonempty heading start drops the heading and preserves both paragraphs");
        note.Key(Key.D2, HeadingShortcut);
        Assert(note.Markdown == "正文\n\n## 标题", "heading keyboard shortcut applies to the current paragraph");
        note.Editor.Undo();
        Assert(note.Markdown == "正文\n\n标题", "undo restores heading format without a text change");
        note.Editor.Redo();
        note.Select(note.Body(1), note.Body(1));
        note.Key(Key.Delete);
        note.Type("重写");
        Assert(note.Markdown == "正文\n\n重写", "deleting the selected heading text resets typing format");

        note = new EditorSession("## 标题");
        note.End(note.Body());
        note.Key(Key.Enter);
        note.Type("正文");
        Assert(note.Markdown == "## 标题\n\n正文" && note.Body(1).FontSize == 14,
               "Enter at heading end begins a body paragraph");
        note = new EditorSession("## 标题");
        note.Start(note.Body());
        note.Key(Key.Enter);
        Assert(note.Markdown == "<br>\n\n## 标题" && note.Editor.CaretPosition.Paragraph == note.Body(1),
               "Enter at heading start inserts body space and keeps the title");
        note = new EditorSession("## 左右");
        note.Editor.CaretPosition = note.Body().Inlines.FirstInline.ContentStart.GetPositionAtOffset(1);
        note.Key(Key.Enter);
        Assert(note.Markdown == "## 左\n\n## 右", "Enter within a heading keeps the format of both fragments");
        note = new EditorSession("");
        note.Type("##", drain: false);
        note.Key(Key.Space);
        note.Key(Key.Enter);
        note.Type("正文");
        Assert(note.Markdown == "正文", "Enter on an empty heading resets the current line");
        note = new EditorSession("");
        note.Type("##", drain: false);
        note.Key(Key.Space, drain: false);
        note.Type("快速输入", drain: false);
        note.Key(Key.Enter);
        Assert(note.Markdown == "## 快速输入\n\n<br>", $"queued typing, Markdown conversion and Enter retain input order ({note.Markdown.Replace("\n", "|")})");

        note = new EditorSession("格式");
        note.Select(note.Body(), note.Body());
        note.Key(Key.B, ModifierKeys.Control);
        note.Key(Key.I, ModifierKeys.Control);
        note.Key(Key.X, FormatShortcut);
        note.Key(Key.U, ModifierKeys.Control);
        Assert(note.Markdown == "<s><u><em><strong>格式</strong></em></u></s>",
               "inline keyboard shortcuts combine bold, italic, strike and underline");

        note = new EditorSession("- 第一项\n- 第二项\n- 第三项");
        var second = note.Row(0, 1);
        note.Select(second, note.Row(0, 2), excludeEnd: true);
        note.Key(Key.D7, FormatShortcut);
        Assert(note.Markdown == "- 第一项\n\n1. 第二项\n\n- 第三项" &&
               note.Editor.Selection.Start.Paragraph == second &&
               note.Editor.Selection.End.Paragraph == note.Row(2),
               "numbering applies only to selected rows and keeps the selection");
        note.Editor.Undo();
        Assert(note.Markdown == "- 第一项\n- 第二项\n- 第三项", "one undo restores the original list and its markers");
        note.Editor.Redo();
        Assert(note.Markdown.Contains("1. 第二项", StringComparison.Ordinal), "redo reapplies the row conversion");

        note = new EditorSession("- 父项\n- 子项甲\n- 子项乙\n- 下一项");
        note.Start(note.Row(0));
        note.Key(Key.Tab);
        Assert(note.List(0).ListItems.Count == 4, "Tab on the first item keeps list structure");
        note.Select(note.Row(0, 1), note.Row(0, 2));
        note.Key(Key.Tab);
        var nested = note.List(0).ListItems.FirstListItem.Blocks.LastBlock as List;
        Assert(nested?.ListItems.Count == 2 && note.List(0).ListItems.Count == 2, "Tab indents a multi-row selection");
        var reopened = new EditorSession(note.Markdown);
        Assert(reopened.List(0).ListItems.FirstListItem.Blocks.LastBlock is List savedList && savedList.ListItems.Count == 2,
               "nested Markdown list structure survives reopening");
        note.Key(Key.Tab, ModifierKeys.Shift);
        Assert(note.List(0).ListItems.Count == 4, "Shift+Tab lifts the selected rows one level");

        note = new EditorSession("- 父项\n- [x] 待办子项\n- 普通子项");
        note.Select(note.Row(1), note.Row(2));
        note.Key(Key.OemCloseBrackets, ModifierKeys.Control);
        var parent = note.List(0).ListItems.FirstListItem;
        Assert(note.Editor.Document.Blocks.Count == 1 && parent.Blocks.OfType<List>().Count() == 2 &&
               parent.Blocks.OfType<List>().First().MarkerStyle == TextMarkerStyle.None &&
               note.Markdown.Contains("- [x] 待办子项", StringComparison.Ordinal),
               "indent moves adjacent tasks and bullets to the same level under the previous row");
        note.Key(Key.OemOpenBrackets, ModifierKeys.Control);
        Assert(note.Markdown == "- 父项\n\n- [x] 待办子项\n\n- 普通子项",
               "outdent restores a mixed selection without changing order or task state");

        note = new EditorSession("- 父项\n  - 子项");
        var child = (Paragraph)((List)note.List(0).ListItems.FirstListItem.Blocks.LastBlock).ListItems.FirstListItem.Blocks.FirstBlock;
        note.Start(child);
        note.Key(Key.Back);
        Assert(note.Markdown == "- 父项\n- 子项", "Backspace at a nested item start lifts it one level");

        note = new EditorSession("3. 初始");
        note.End(note.Row(0));
        note.Key(Key.Enter);
        note.Type("续写");
        Assert(note.Markdown == "3. 初始\n4. 续写", "Enter continues numbering from the original start");
        note.Key(Key.Enter, ModifierKeys.Shift);
        note.Type("同项换行");
        Assert(note.List(0).ListItems.Count == 2 && note.Markdown.Contains("4. 续写<br>同项换行", StringComparison.Ordinal),
               "Shift+Enter inserts a line break within the item");
        note.Key(Key.Enter);
        note.Key(Key.Enter);
        note.Type("正文");
        Assert(note.Markdown == "3. 初始\n4. 续写<br>同项换行\n\n正文",
               $"Enter on an empty root item exits the list ({note.Markdown.Replace("\n", "|")})");

        note = new EditorSession("3. 一组\n\n7) 另一组");
        note.End(note.Row(1));
        note.Key(Key.Enter);
        note.Type("续写");
        reopened = new EditorSession(note.Markdown);
        Assert(reopened.List(0).StartIndex == 3 && reopened.List(1).StartIndex == 7 &&
               reopened.List(1).ListItems.Count == 2, "separate ordered starts survive Markdown storage");

        note = new EditorSession("");
        note.Type("5.", drain: false);
        note.Key(Key.Space, drain: false);
        note.Type("键盘编号");
        note.End(note.Row(0));
        note.Key(Key.Enter);
        var numberedPaste = new DataObject(DataFormats.UnicodeText, "1. 粘贴甲\n2. 粘贴乙");
        var pasteEvent = new DataObjectPastingEventArgs(numberedPaste, false, DataFormats.UnicodeText);
        note.Editor.RaiseEvent(pasteEvent);
        Assert(pasteEvent.CommandCancelled && note.Markdown == "5. 键盘编号\n6. 粘贴甲\n7. 粘贴乙",
               "pasting a list into an empty item continues the existing numbering");

        note = new EditorSession("");
        note.Type("-", drain: false);
        note.Key(Key.Space);
        note.Type("[x]", drain: false);
        note.Key(Key.Space, drain: false);
        note.Type("键盘待办");
        Assert(note.Markdown == "- [x] 键盘待办" && note.List(0).MarkerStyle == TextMarkerStyle.None,
               "typing a Markdown task inside a bullet creates one checked marker");

        note = new EditorSession("- [x] 完成\n- [ ] 后续");
        note.End(note.Row(0));
        note.Key(Key.Enter);
        note.Type("续写");
        Assert(note.Markdown == "- [x] 完成\n- [ ] 续写\n- [ ] 后续",
               "Enter continues a checked task with a fresh unchecked task");
        note.Start(note.Row(0, 1));
        note.Key(Key.Back);
        Assert(note.Markdown == "- [x] 完成\n\n续写\n\n- [ ] 后续",
               "Backspace at task text start removes the marker without deleting content");
        note.Start(note.Body(1));
        note.Key(Key.D9, FormatShortcut);
        Assert(note.Markdown == "- [x] 完成\n- [ ] 续写\n- [ ] 后续", "task shortcut joins adjacent tasks and preserves checked states");

        note = new EditorSession("- [x] 完成");
        note.Start(note.Row(0));
        note.Key(Key.Enter);
        Assert(note.Markdown == "- [ ] <br>\n- [x] 完成" && note.Editor.CaretPosition.Paragraph == note.Row(0, 1),
               "Enter at a checked task's start leaves its status with the text");
        reopened = new EditorSession(note.Markdown);
        Assert(reopened.List(0).ListItems.Count == 2 && reopened.Row(0).Inlines.FirstInline is InlineUIContainer { Child: CheckBox { IsChecked: false } },
               "empty task survives saving and reopening");
        reopened.Start(reopened.Row(0));
        reopened.Key(Key.Enter);
        Assert(reopened.Markdown == "<br>\n\n- [x] 完成", "Enter on an empty reopened task exits the list cleanly");

        note = new EditorSession("- [x] 甲乙\n- [ ] 丙丁");
        note.Editor.Selection.Select(note.Row(0).Inlines.OfType<Run>().Single().ContentStart.GetPositionAtOffset(1),
                                     note.Row(0, 1).Inlines.OfType<Run>().Single().ContentStart.GetPositionAtOffset(1));
        note.Key(Key.Back);
        Assert(note.Markdown == "- [x] 甲丁" &&
               note.Row(0).Inlines.OfType<InlineUIContainer>().Count() == 1, "deleting across tasks leaves one checkbox and preserves the leading state");

        note = new EditorSession("- 数值恢复默认值\n- 检查曲线面板状态\n- 检查 UI 逻辑\n- [x] 保存设计参考");
        note.End(note.Row(1));
        note.Key(Key.Enter);
        note.Type("通过底栏创建的项目");
        note.Key(Key.D8, FormatShortcut);
        Assert(note.Markdown.EndsWith("- 通过底栏创建的项目", StringComparison.Ordinal), "bullet shortcut can change one task back to a bullet");
        note.Key(Key.Enter);
        note.Type("新一项");
        var screenshot = Path.Combine(output, "list-editing-preview.png");
        Render((FrameworkElement)note.Window.Content, 320, 320, screenshot);
        var rows = new[] { note.Row(0, 0), note.Row(0, 1), note.Row(0, 2), note.Row(1), note.Row(2), note.Row(2, 1) };
        var rects = rows.Select(row => row.Inlines.OfType<Run>().First().ContentStart.GetCharacterRect(LogicalDirection.Forward)).ToArray();
        Assert(rects.All(rect => !rect.IsEmpty) && rects.Max(rect => rect.Left) - rects.Min(rect => rect.Left) <= 1,
               "rendered text starts align for imported bullets, task and newly created bullets");
        var spacing = rects.Zip(rects.Skip(1), (first, next) => next.Top - first.Top).ToArray();
        Assert(spacing.Min() > 0 && spacing.Max() - spacing.Min() <= 2,
               "rendered list spacing is consistent across marker types");
        Assert(note.List(1).MarkerStyle == TextMarkerStyle.None, "task marker is rendered without a second bullet");
        var store = new NoteStore(Path.Combine(temp, "editor"));
        store.Save([note.Window.Data]);
        reopened = new EditorSession(store.Load().Single().Markdown);
        Assert(reopened.Markdown == note.Markdown && reopened.List(2).ListItems.Count == 2,
               "the edited list and checked task round-trip through the note store");
    }

    private sealed class EditorSession
    {
        private readonly OfflineKeyboard keyboard = new();
        private readonly OfflineInputSource input;
        internal Window2 Window { get; }
        internal RichTextBox Editor { get; }
        internal string Markdown => Window.Data.Markdown;

        internal EditorSession(string markdown)
        {
            Window = new Window2(new NoteData { Markdown = markdown });
            Editor = (RichTextBox)typeof(Window2).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Window);
            var surface = (FrameworkElement)Window.Content;
            surface.Measure(new Size(320, 320));
            surface.Arrange(new Rect(0, 0, 320, 320));
            surface.UpdateLayout();
            input = new OfflineInputSource { RootVisual = Editor };
            Invoke(Window, "BeginEditing");
        }

        internal Paragraph Body(int index = 0) => (Paragraph)Editor.Document.Blocks.ElementAt(index);
        internal List List(int index) => (List)Editor.Document.Blocks.ElementAt(index);
        internal Paragraph Row(int listIndex, int rowIndex = 0)
            => (Paragraph)List(listIndex).ListItems.ElementAt(rowIndex).Blocks.FirstBlock;

        private static TextPointer TextStart(Paragraph paragraph) =>
            (paragraph.Inlines.FirstInline is InlineUIContainer marker ? marker.ElementEnd : paragraph.ContentStart)
            .GetInsertionPosition(LogicalDirection.Forward);

        internal void Start(Paragraph paragraph) => Editor.CaretPosition = TextStart(paragraph);
        internal void End(Paragraph paragraph) => Editor.CaretPosition = paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        internal void Select(Paragraph first, Paragraph last, bool excludeEnd = false)
            => Editor.Selection.Select(TextStart(first), excludeEnd ? TextStart(last) : last.ContentEnd);

        internal void Key(Key key, ModifierKeys modifiers = ModifierKeys.None, bool drain = true)
        {
            keyboard.ActiveModifiers = modifiers;
            var args = new KeyEventArgs(keyboard, input, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            Editor.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = Keyboard.KeyDownEvent;
                Editor.RaiseEvent(args);
            }
            keyboard.ActiveModifiers = ModifierKeys.None;
            if (drain) Drain();
        }

        internal void Type(string text, bool drain = true)
        {
            var composition = new TextComposition(InputManager.Current, Editor, text);
            Editor.RaiseEvent(new TextCompositionEventArgs(keyboard, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
            if (drain) Drain();
        }

        private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
