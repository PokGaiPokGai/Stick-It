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

    private static string WriteBlocks(BlockCollection blocks, bool insideList = false)
        => string.Join("\n\n", blocks.Select(block => WriteBlock(block, insideList)));

    private static string WriteBlock(Block block, bool insideList)
    {
        switch (block)
        {
            case Paragraph paragraph:
                if (paragraph.Tag is CodeBlockInfo code)
                {
                    var value = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Replace("\r\n", "\n");
                    var fence = CodeFence(value, 3);
                    return fence + code.Language + "\n" + value + "\n" + fence;
                }

                var text = WriteInlines(paragraph.Inlines);
                if (paragraph.Tag is BlockStyle.Divider)
                    return text.Length == 0 ? "---" : "---\n\n" + text;
                var level = HeadingLevel(paragraph);
                if (level > 0) return new string('#', level) + " " + text;
                if (!insideList && (text.StartsWith("[ ] ", StringComparison.Ordinal) || text.StartsWith("[x] ", StringComparison.Ordinal)))
                    return "- " + text;
                return text.Length == 0 ? "<br>" : text;
            case Section section:
                var children = WriteBlocks(section.Blocks, insideList);
                return section.Tag is BlockStyle.Quote || section.BorderThickness.Left > 0
                    ? string.Join("\n", children.Split('\n').Select(line => "> " + line))
                    : children;
            case List list:
                var number = list.StartIndex;
                var items = new List<string>();
                foreach (var item in list.ListItems)
                {
                    var prefix = list.MarkerStyle == TextMarkerStyle.Decimal
                        ? (number++).ToString(CultureInfo.InvariantCulture) + ". "
                        : "- ";
                    var lines = WriteBlocks(item.Blocks, insideList: true).Split('\n');
                    var indent = new string(' ', prefix.Length);
                    items.Add(prefix + string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line)));
                }
                return string.Join("\n", items);
            case Table table:
                return WriteTable(table);
            default:
                return Escape(new TextRange(block.ContentStart, block.ContentEnd).Text);
        }
    }

    private static string WriteTable(Table table)
    {
        var rows = table.RowGroups.SelectMany(group => group.Rows)
            .Select(row => row.Cells.Select(cell => WriteBlocks(cell.Blocks).Replace("\n", " ")).ToArray())
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

    private static string WriteInlines(InlineCollection inlines, InlineStyle inherited = InlineStyle.None)
    {
        var result = new StringBuilder();
        foreach (var inline in inlines)
        {
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
                    result.Append(WriteRun(run.Text, style));
                    break;
                case LineBreak:
                    result.Append("<br>");
                    break;
                case InlineUIContainer { Child: CheckBox check }:
                    result.Append(check.IsChecked == true ? "[x] " : "[ ] ");
                    break;
                case Span span:
                    var content = WriteInlines(span.Inlines, style);
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
