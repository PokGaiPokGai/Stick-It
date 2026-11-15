using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestNotePreviews(string temp, string output)
    {
        var main = new MainWindow(new NoteStore(Path.Combine(temp, "previews")));
        var note = (Window2)Invoke(main, "AddNote", new NoteData
        {
            Markdown = "**blender HaiMesh插件问题**\n\n- 数值reset to default value不行\n- 实例不保存上下文/状态\n- ui逻辑\n- crown卡死问题",
            Modified = DateTime.Today.AddHours(23).AddMinutes(43)
        });
        var tasks = (Window2)Invoke(main, "AddNote", new NoteData
        {
            Markdown = "## 今日待办\n\n- [ ] 整理参考\n- [x] **保存修改**\n- [ ] 下一步",
            Color = "#E2F1FF",
            Modified = note.Data.Modified.AddMinutes(-1)
        });
        var numbered = (Window2)Invoke(main, "AddNote", new NoteData
        {
            Markdown = "3. 第一项\n4. 第二项\n   - 嵌套项目\n5. 第三项",
            Color = "#F5E0F0",
            Modified = note.Data.Modified.AddMinutes(-2)
        });
        Invoke(main, "RefreshList");
        Render((FrameworkElement)main.Content, 478, 720, Path.Combine(output, "formatted-library-preview.png"));
        var cards = ((ItemsControl)main.FindName("NoteList")).Items.OfType<NoteCard>().ToArray();
        var preview = cards[0].Preview;
        var list = preview.Blocks.OfType<List>().Single();
        var paragraphs = list.ListItems.Select(item => (Paragraph)item.Blocks.FirstBlock).ToArray();
        Assert(list.MarkerStyle == TextMarkerStyle.Disc && paragraphs.Length == 4 &&
               paragraphs.All(paragraph => paragraph.TextIndent == 0) &&
               paragraphs.Select(paragraph => paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward).Left).Distinct().Count() == 1,
               "library preview retains all bullet markers and aligns the first and following items");
        var first = (Paragraph)preview.Blocks.FirstBlock;
        Assert(first.Inlines.OfType<Run>().Any(run => run.FontWeight == FontWeights.Bold) &&
               paragraphs[0].Inlines.OfType<Run>().All(run => run.FontWeight == FontWeights.Normal),
               "preview retains authored bold text without forcing the first list item into a title");
        var taskList = cards[1].Preview.Blocks.OfType<List>().Single();
        var taskRows = taskList.ListItems.Select(item => (Paragraph)item.Blocks.FirstBlock).ToArray();
        Assert(taskList.MarkerStyle == TextMarkerStyle.None &&
               taskRows.Select(row => TaskCheckbox(row).IsChecked).SequenceEqual(new bool?[] { false, true, false }) &&
               ShowsTaskStrike(taskRows[1]) && !ShowsTaskStrike(taskRows[0]),
               "preview retains task checkboxes and completed text strikethrough");
        var numberedList = cards[2].Preview.Blocks.OfType<List>().Single();
        Assert(numberedList.MarkerStyle == TextMarkerStyle.Decimal && numberedList.StartIndex == 3 &&
               numberedList.ListItems.ElementAt(1).Blocks.OfType<List>().Single().MarkerStyle == TextMarkerStyle.Disc,
               "preview retains ordered starts and nested list structure");
        Assert(note.Data.Markdown.StartsWith("**blender", StringComparison.Ordinal) &&
               tasks.Data.Markdown.Contains("- [x] **保存修改**", StringComparison.Ordinal) &&
               numbered.Data.Markdown.StartsWith("3.", StringComparison.Ordinal),
               "generating and rendering previews leaves note content intact");

        var plain = new NoteCard(new Window2(new NoteData { Markdown = "普通正文\n第二行" })).Preview;
        Assert(((Paragraph)plain.Blocks.FirstBlock).FontWeight == FontWeights.Normal,
               "a plain-text note preview does not invent a bold title");
        var blank = new NoteCard(new Window2(new NoteData())).Preview;
        Assert(new TextRange(blank.ContentStart, blank.ContentEnd).Text.TrimEnd() == "空白便签",
               "empty notes retain their placeholder in the library");
        var structured = new NoteCard(new Window2(new NoteData
        {
            Markdown = "> 引用 **重点**\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```cs\nvar x = 1;\n```"
        })).Preview;
        Assert(structured.Blocks.OfType<Section>().Any() && structured.Blocks.OfType<Table>().Any(),
               "bounded previews preserve quote and table structure");
    }
}
