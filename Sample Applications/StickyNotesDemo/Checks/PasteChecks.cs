using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestMarkdownPaste(string temp)
    {
        const string table = "| 原表 | 值 |\n| --- | --- |\n| 原行 | 保留 |";
        var longMarkdown = "## 长文开头\n\n" + string.Join("\n\n", Enumerable.Range(1, 96).Select(index =>
            $"### 小节 {index}\n\n**关键点** {new string('文', 400)}")) +
            "\n\n- [x] 长文待办\n\n> 长文引用\n\n| 列名 | 值 |\n| --- | --- |\n| 最后一行 | 数据 |";

        var note = new EditorSession(table);
        EditingCommands.MoveToDocumentStart.Execute(null, note.Editor);
        Assert(note.Editor.CaretPosition.Paragraph == null, "WPF allows the caret before the first table");
        Assert(PasteMarkdown(note, longMarkdown), "long Markdown pastes at the table boundary");
        Assert(note.Markdown.StartsWith("## 长文开头", StringComparison.Ordinal) &&
               note.Markdown.Contains("### 小节 96", StringComparison.Ordinal) &&
               note.Markdown.Contains("- [x] 长文待办", StringComparison.Ordinal) &&
               note.Editor.Document.Blocks.OfType<Table>().Count() == 2 &&
               note.Editor.Document.Blocks.LastBlock is Table &&
               note.Markdown.EndsWith("| 原行 | 保留 |", StringComparison.Ordinal),
               "the pasted document keeps all sections, formatting and the original table");
        note.Editor.Undo();
        Assert(note.Editor.Document.Blocks.Count == 1 && note.Editor.Document.Blocks.FirstBlock is Table,
               "one undo restores the table and removes the entire paste");
        note.Editor.Redo();
        Assert(note.Markdown.Contains("### 小节 96", StringComparison.Ordinal),
               "redo restores the full Markdown paste");

        var store = new NoteStore(Path.Combine(temp, "long-markdown-paste"));
        store.Save([note.Window.Data]);
        var reopened = MarkdownView.Render(store.Load().Single().Markdown);
        Assert(reopened.Blocks.OfType<Table>().Count() == 2 &&
               new TextRange(reopened.ContentStart, reopened.ContentEnd).Text.Contains("最后一行", StringComparison.Ordinal),
               "long pasted Markdown saves and reopens with both tables");

        note = new EditorSession(table);
        EditingCommands.MoveToDocumentStart.Execute(null, note.Editor);
        Assert(PasteMarkdown(note, "**表格前的粗体**") && note.Editor.Document.Blocks.FirstBlock is Paragraph &&
               note.Markdown.StartsWith("<strong>表格前的粗体</strong>", StringComparison.Ordinal),
               "inline Markdown also pastes before a table");

        note = new EditorSession(table);
        EditingCommands.MoveToDocumentEnd.Execute(null, note.Editor);
        Assert(note.Editor.CaretPosition.Paragraph == null, "WPF allows the caret at the last table row boundary");
        Assert(PasteMarkdown(note, "## 表格后标题\n\n正文") && note.Editor.Document.Blocks.FirstBlock is Table &&
               note.Markdown.EndsWith("## 表格后标题\n\n正文", StringComparison.Ordinal),
               "multi-block Markdown also pastes after the last table row");

        note = new EditorSession(table);
        note.Editor.SelectAll();
        Assert(PasteMarkdown(note, "## 替换标题\n\n- 新项目") &&
               note.Markdown == "## 替换标题\n\n- 新项目",
               "pasting replaces an entirely selected structured note");

        note = new EditorSession("");
        note.Editor.Document.Blocks.Clear();
        Assert(PasteMarkdown(note, "## 新便签标题\n\n正文") && note.Markdown == "## 新便签标题\n\n正文",
               "multi-block Markdown pastes into an empty note");

        note = new EditorSession("前文 [原链接](https://example.com) 后文");
        var link = note.Body().Inlines.OfType<Hyperlink>().Single();
        note.Editor.CaretPosition = link.Inlines.FirstInline.ContentStart.GetPositionAtOffset(1);
        Assert(PasteMarkdown(note, "## 插入标题\n\n- 插入项目") &&
               note.Markdown.Contains("[原链接](<https://example.com>)", StringComparison.Ordinal) &&
               note.Markdown.Contains("## 插入标题\n\n- 插入项目", StringComparison.Ordinal) &&
               note.Markdown.EndsWith("后文", StringComparison.Ordinal),
               "pasting Markdown blocks inside a link preserves the original link and following text");

        note = new EditorSession("[原链接](https://example.com)");
        link = (Hyperlink)note.Body().Inlines.FirstInline;
        note.Editor.CaretPosition = link.Inlines.FirstInline.ContentStart.GetPositionAtOffset(1);
        Assert(PasteMarkdown(note, "[新链接](https://example.org)") &&
               note.Markdown == "[原链接](<https://example.com>)[新链接](<https://example.org>)",
               "pasting a link at another link keeps both hyperlinks");
    }

    private static bool PasteMarkdown(EditorSession note, string markdown)
    {
        var clipboard = new DataObject(DataFormats.UnicodeText, markdown);
        var paste = new DataObjectPastingEventArgs(clipboard, false, DataFormats.UnicodeText);
        // Use the real command transaction with a local data object in
        // place of WPF's access to the system clipboard.
        var binding = new CommandBinding(ApplicationCommands.Paste, (_, args) =>
        {
            note.Editor.RaiseEvent(paste);
            args.Handled = true;
        }, (_, args) =>
        {
            args.CanExecute = true;
            args.Handled = true;
        });
        note.Editor.CommandBindings.Add(binding);
        try { ApplicationCommands.Paste.Execute(null, note.Editor); }
        finally { note.Editor.CommandBindings.Remove(binding); }
        DrainFormatting();
        return paste.CommandCancelled;
    }
}
