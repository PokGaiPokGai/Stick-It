using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

internal static class MarkdownWriter
{
    private const char Backtick = (char)0x60;
    private static readonly Regex MarkdownPunctuation = new(@"[\x60\\*_{}\[\]()<>#+\-.!|~&=^]");

    internal static string Write(FlowDocument document)
    {
        var text = WriteBlocks(document.Blocks);
        return text == "<br>" ? "" : text;
    }

    internal static string WriteSelection(FlowDocument document, TextRange selection)
    {
        if (selection.IsEmpty) return "";
        if (selection.Start.Paragraph is { } paragraph && paragraph == selection.End.Paragraph &&
            (selection.Start.CompareTo(paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward)) > 0 ||
             selection.End.CompareTo(paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)) < 0))
            return WriteInlines(paragraph.Inlines, selection: selection);
        return WriteBlocks(document.Blocks, selection: selection);
    }

    private static bool Included(TextElement element, TextRange selection) => selection == null ||
        selection.Start.CompareTo(element.ContentEnd) < 0 && selection.End.CompareTo(element.ContentStart) > 0;

    private static TextRange SelectedRange(TextElement element, TextRange selection) => new(
        selection != null && selection.Start.CompareTo(element.ContentStart) > 0 ? selection.Start : element.ContentStart,
        selection != null && selection.End.CompareTo(element.ContentEnd) < 0 ? selection.End : element.ContentEnd);

    private static string WriteBlocks(BlockCollection blocks, bool insideList = false, TextRange selection = null)
    {
        var output = new List<string>();
        var previousDelimiter = ' ';
        foreach (var block in blocks)
        {
            if (!Included(block, selection)) continue;
            // CommonMark joins adjacent ordered lists with the same delimiter.
            // Use the other delimiter when the editor has a separate start.
            var delimiter = previousDelimiter == '.' ? ')' : '.';
            output.Add(WriteBlock(block, insideList, delimiter, selection));
            previousDelimiter = block is List { MarkerStyle: TextMarkerStyle.Decimal } ? delimiter : ' ';
        }
        return string.Join("\n\n", output);
    }

    private static string WriteBlock(Block block, bool insideList, char orderedDelimiter, TextRange selection)
    {
        switch (block)
        {
            case Paragraph paragraph:
                if (FormatOf(paragraph) is CodeBlockInfo code)
                {
                    var range = SelectedRange(paragraph, selection);
                    var value = NoteLists.InlineText(range.Start, range.End).Replace("\r\n", "\n");
                    var fence = CodeFence(value, 3);
                    return fence + code.Language + "\n" + value + "\n" + fence;
                }

                var text = WriteInlines(paragraph.Inlines, selection: selection);
                if (selection != null && NoteLists.TaskOf(paragraph) is { Child: CheckBox task } marker && !Included(marker, selection))
                    text = (task.IsChecked == true ? "[x] " : "[ ] ") + text;
                if (FormatOf(paragraph) is BlockStyle.Divider)
                    return text.Length == 0 ? "---" : "---\n\n" + text;
                var level = HeadingLevel(paragraph);
                if (level > 0) return new string('#', level) + " " + text;
                if (!insideList && (text.StartsWith("[ ] ", StringComparison.Ordinal) || text.StartsWith("[x] ", StringComparison.Ordinal)))
                    return "- " + text;
                if (text is "[ ] " or "[x] ")
                    return text + "<br>";
                return text.Length == 0 ? "<br>" : text;
            case Section section:
                var children = WriteBlocks(section.Blocks, insideList, selection);
                return section.Tag is BlockStyle.Quote || section.BorderThickness.Left > 0
                    ? string.Join("\n", children.Split('\n').Select(line => "> " + line))
                    : children;
            case List list:
                var number = list.StartIndex;
                var items = new List<string>();
                foreach (var item in list.ListItems)
                {
                    var prefix = list.MarkerStyle == TextMarkerStyle.Decimal
                        ? (number++).ToString(CultureInfo.InvariantCulture) + orderedDelimiter + " "
                        : "- ";
                    if (!Included(item, selection)) continue;
                    var lines = WriteBlocks(item.Blocks, insideList: true, selection).Split('\n');
                    var indent = new string(' ', prefix.Length);
                    items.Add(prefix + string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line)));
                }
                return string.Join("\n", items);
            case Table table:
                return WriteTable(table, selection);
            default:
                return Escape(SelectedRange(block, selection).Text);
        }
    }

    private static string WriteTable(Table table, TextRange selection)
    {
        var rows = table.RowGroups.SelectMany(group => group.Rows).Where(row => Included(row, selection))
            .Select(row => row.Cells.Where(cell => Included(cell, selection))
                .Select(cell => WriteBlocks(cell.Blocks, selection: selection).Replace("\n", " ")).ToArray())
            .ToArray();
        if (rows.Length == 0) return "";
        var columns = rows.Max(row => row.Length);
        var result = new List<string>();
        for (var i = 0; i < rows.Length; i++)
        {
            result.Add("| " + string.Join(" | ", rows[i].Concat(Enumerable.Repeat("", columns - rows[i].Length))) + " |");
            if (i == 0) result.Add("| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |");
        }
        return string.Join("\n", result);
    }

    private static string WriteInlines(InlineCollection inlines, InlineStyle inherited = InlineStyle.None, TextRange selection = null)
    {
        var result = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (!Included(inline, selection)) continue;
            var style = inherited;
            // Hyperlinks already supply their own underline.
            if (inline is not Hyperlink && inline.TextDecorations is { } decorations)
            {
                foreach (var decoration in decorations)
                {
                    if (decoration.Location == TextDecorationLocation.Underline) style |= InlineStyle.Underline;
                    if (decoration.Location == TextDecorationLocation.Strikethrough) style |= InlineStyle.Strike;
                }
            }

            switch (inline)
            {
                case Run run:
                    if (run.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight()) style |= InlineStyle.Bold;
                    if (run.FontStyle != FontStyles.Normal) style |= InlineStyle.Italic;
                    if (run.FontFamily.Source == CodeFont.Source) style |= InlineStyle.Code;
                    var text = run.Text;
                    if (selection != null)
                    {
                        var selected = SelectedRange(run, selection);
                        text = NoteLists.InlineText(selected.Start, selected.End);
                    }
                    result.Append(WriteRun(text, style));
                    break;
                case LineBreak:
                    result.Append("<br>");
                    break;
                case InlineUIContainer { Child: CheckBox check }:
                    result.Append(check.IsChecked == true ? "[x] " : "[ ] ");
                    break;
                case Span span:
                    var content = WriteInlines(span.Inlines, style, selection);
                    var link = span.Tag as LinkInfo;
                    var url = link?.Url ?? (span as Hyperlink)?.NavigateUri?.OriginalString;
                    result.Append(url == null ? content : (link?.IsImage == true ? "!" : "") +
                        "[" + content + "](<" + url.Replace("<", "%3C").Replace(">", "%3E") + ">)");
                    break;
            }
        }
        return result.ToString();
    }

    private static string WriteRun(string text, InlineStyle style)
    {
        if (text.Length == 0) return "";
        string value;
        if (style.HasFlag(InlineStyle.Code) && !text.Contains('\n') && !string.IsNullOrWhiteSpace(text))
        {
            var fence = CodeFence(text, 1);
            var pad = text[0] == Backtick || text[^1] == Backtick || (text[0] == ' ' && text[^1] == ' ') ? " " : "";
            value = fence + pad + text + pad + fence;
        }
        else value = Escape(text);

        // Inline tags keep adjacent and overlapping styles unambiguous on reload.
        if (style.HasFlag(InlineStyle.Bold)) value = "<strong>" + value + "</strong>";
        if (style.HasFlag(InlineStyle.Italic)) value = "<em>" + value + "</em>";
        if (style.HasFlag(InlineStyle.Underline)) value = "<u>" + value + "</u>";
        if (style.HasFlag(InlineStyle.Strike)) value = "<s>" + value + "</s>";
        return value;
    }

    private static string Escape(string text)
        => MarkdownPunctuation.Replace(text, match => "\\" + match.Value).Replace("\r\n", "\n").Replace("\n", "<br>");

    private static string CodeFence(string value, int minimum)
    {
        var longest = 0;
        var run = 0;
        foreach (var character in value)
        {
            run = character == Backtick ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return new string(Backtick, Math.Max(longest + 1, minimum));
    }
}
