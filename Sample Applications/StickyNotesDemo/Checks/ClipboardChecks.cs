using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using StickyNotesDemo;

internal static partial class Checks
{
    private static IDataObject CopySelection(EditorSession source)
    {
        IDataObject result = null;
        DataObjectCopyingEventHandler capture = (_, e) => { result = e.DataObject; e.CancelCommand(); };
        DataObject.AddCopyingHandler(source.Editor, capture);
        try { ApplicationCommands.Copy.Execute(null, source.Editor); }
        finally { DataObject.RemoveCopyingHandler(source.Editor, capture); }
        return result;
    }

    private static void PasteSelection(EditorSession target, IDataObject data)
    {
        var nativeFormat = data.GetDataPresent(DataFormats.XamlPackage) ? DataFormats.XamlPackage : DataFormats.Xaml;
        var args = new DataObjectPastingEventArgs(data, false, nativeFormat);
        target.Editor.RaiseEvent(args);
        Assert(args.CommandCancelled, "internal paste is handled without the lossy native rich-text path");
        DrainFormatting();
    }

    private static void TestInternalClipboard()
    {
        foreach (var markdown in new[]
        {
            "## Heading", "- [x] task\n- [ ] next", "```cs\nvar x = 1;\nvar y = 2;\n```",
            "## Title\n\n> **quote**\n\n- parent\n  - [x] child\n\n3. third\n4. fourth\n\n| A | B |\n| --- | --- |\n| one | `two` |",
            "**bold** *italic* <u>under</u> ~~strike~~ `code` [link](https://example.com)",
            "before\n\n<br>\n\nafter"
        })
        {
            var source = new EditorSession(markdown);
            source.Editor.SelectAll();
            var data = CopySelection(source);
            var target = new EditorSession("");
            PasteSelection(target, data);
            var expected = MarkdownWriter.Write(MarkdownView.Render(markdown));
            Assert(target.Markdown == expected, $"internal clipboard preserves block and inline semantics: {markdown.Split('\n')[0]} (actual={target.Markdown})");
            target.Editor.Undo();
            Assert(target.Markdown == "", "internal paste is one undo unit");
            target.Editor.Redo();
            Assert(target.Markdown == expected && MarkdownWriter.Write(MarkdownView.Render(target.Markdown)) == expected,
                "redo and reopening preserve the pasted document");
        }

        var partial = new EditorSession("## before **selected** after");
        var run = partial.Body().Inlines.OfType<Run>().Single(item => item.Text == "selected");
        partial.Editor.Selection.Select(run.ContentStart, run.ContentEnd);
        var copy = CopySelection(partial);
        var inlineTarget = new EditorSession("prefix suffix");
        inlineTarget.Editor.CaretPosition = ((Run)inlineTarget.Body().Inlines.FirstInline).ContentStart.GetPositionAtOffset(7);
        PasteSelection(inlineTarget, copy);
        Assert(inlineTarget.Markdown == "prefix <strong>selected</strong>suffix",
            "a partial heading selection pastes inline at the caret without copying its surrounding text or heading");

        var list = new EditorSession("3. third\n4. fourth\n5. fifth");
        list.Select(list.Row(0, 1), list.Row(0, 2));
        var listTarget = new EditorSession("");
        PasteSelection(listTarget, CopySelection(list));
        Assert(listTarget.Markdown == "4. fourth\n5. fifth", "copying selected numbered rows retains their starting number");

        var tasks = new EditorSession("- [x] first\n- [ ] second\n- [x] third");
        tasks.Select(tasks.Row(0), tasks.Row(0, 1));
        var taskTarget = new EditorSession("");
        PasteSelection(taskTarget, CopySelection(tasks));
        Assert(taskTarget.Markdown == "- [x] first\n- [ ] second", "multi-row label selection retains each task state without copying unselected rows");
    }
}
