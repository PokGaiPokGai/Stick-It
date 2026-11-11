using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.TaskLists;

namespace StickyNotesDemo;

public static class MarkdownView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists()
        .UseEmphasisExtras().UsePreciseSourceLocation().Build();
    private static readonly double[] HeadingSizes = [26, 22, 19, 17, 15, 14];
    private static readonly DependencyProperty ParagraphFormatProperty = DependencyProperty.RegisterAttached(
        "ParagraphFormat", typeof(object), typeof(MarkdownView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure));
    internal static readonly FontFamily CodeFont = new("Consolas");
    internal static readonly Brush InlineCodeBackground = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0));
    internal sealed record CodeBlockInfo(string Language);
    internal sealed record LinkInfo(string Url, bool IsImage);
    internal enum BlockStyle { Quote, Divider }

    [Flags]
    internal enum InlineStyle { None = 0, Bold = 1, Italic = 2, Underline = 4, Strike = 8, Code = 16 }

    public static FlowDocument Render(string markdown)
        => Render(Markdig.Markdown.Parse(markdown, Pipeline));

    internal static FlowDocument ReadFormattedPaste(string text, bool allowIndentedCode)
    {
        var parsed = Markdig.Markdown.Parse(text, Pipeline);
        var formatted = parsed.Any(block => block switch
        {
            HeadingBlock or ListBlock or QuoteBlock or FencedCodeBlock or ThematicBreakBlock or Markdig.Extensions.Tables.Table => true,
            CodeBlock => allowIndentedCode,
            ParagraphBlock { Inline: { } content } => content.Any(inline =>
                inline is EmphasisInline or CodeInline or LinkInline or AutolinkInline ||
                inline is HtmlInline { Tag: "<strong>" or "<em>" or "<u>" or "<s>" or "<br>" }),
            HtmlBlock html => html.Lines.ToString().Trim() == "<br>",
            _ => false
        });
        return formatted ? Render(parsed) : null;
    }

    internal static (int Start, Paragraph Content)? ReadInlineShortcut(string text, bool codeOnly)
    {
        var parsed = Markdig.Markdown.Parse(text, Pipeline);
        if (parsed.Count != 1 || parsed[0] is not ParagraphBlock paragraph ||
            paragraph.Inline?.LastChild is not (EmphasisInline or CodeInline or LinkInline or AutolinkInline) ||
            paragraph.Inline.LastChild.Span.End != text.Length - 1)
            return null;

        var inline = paragraph.Inline.LastChild;
        if (codeOnly && inline is not CodeInline) return null;
        inline.Remove();
        var fragment = new ContainerInline();
        fragment.AppendChild(inline);
        return (inline.Span.Start, Paragraph(fragment));
    }

    private static FlowDocument Render(MarkdownDocument markdown)
    {
        var document = new FlowDocument { FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize=14,
            Foreground=new SolidColorBrush(Color.FromRgb(38,38,38)), PagePadding=new Thickness(0),
            ColumnWidth=double.PositiveInfinity };
        foreach (var block in markdown) document.Blocks.Add(RenderBlock(block));
        if (document.Blocks.Count == 0) document.Blocks.Add(Paragraph(null));
        NoteLists.Normalize(document);
        return document;
    }

    internal static object FormatOf(Paragraph paragraph) => paragraph.GetValue(ParagraphFormatProperty);
    internal static int HeadingLevel(Paragraph paragraph) => FormatOf(paragraph) is int level ? level : 0;

    internal static void SetHeading(Paragraph paragraph, int level)
    {
        // Store the block role in the text tree so WPF undoes it along with
        // changes to the paragraph's font and text.
        if (level == 0)
        {
            paragraph.ClearValue(ParagraphFormatProperty);
            paragraph.ClearValue(TextElement.FontSizeProperty);
            paragraph.ClearValue(TextElement.FontWeightProperty);
        }
        else
        {
            paragraph.SetValue(ParagraphFormatProperty, level);
            paragraph.FontSize = HeadingSizes[level - 1];
            paragraph.FontWeight = FontWeights.SemiBold;
        }
    }

    internal static void SetCodeBlock(Paragraph paragraph, string language)
    {
        paragraph.SetValue(ParagraphFormatProperty, new CodeBlockInfo(language ?? ""));
        paragraph.FontFamily = CodeFont;
        paragraph.FontSize = 12;
        paragraph.Background = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0));
        paragraph.Padding = new Thickness(8);
        paragraph.Margin = new Thickness(0, 4, 0, 10);
    }

    internal static Section Quote() => new()
    {
        Tag = BlockStyle.Quote, BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
        BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0),
        Margin = new Thickness(0, 4, 0, 10)
    };

    internal static InlineUIContainer TaskMarker(bool isChecked) => new(new CheckBox
    {
        IsChecked = isChecked, Focusable = false, Width = NoteLists.TaskWidth,
        Margin = new Thickness(0, 0, NoteLists.TaskGap, 0),
        ToolTip = "标记待办状态", VerticalAlignment = VerticalAlignment.Center
    }) { BaselineAlignment = BaselineAlignment.Center };

    private static System.Windows.Documents.Block RenderBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Paragraph(heading.Inline);
                SetHeading(title, heading.Level);
                return title;
            case ParagraphBlock paragraph: return Paragraph(paragraph.Inline);
            case CodeBlock code:
                var codeParagraph = new Paragraph(new Run(code.Lines.ToString()));
                SetCodeBlock(codeParagraph, code is FencedCodeBlock fenced ? fenced.Info : "");
                return codeParagraph;
            case QuoteBlock quote:
                var section = Quote();
                foreach (var child in quote) section.Blocks.Add(RenderBlock(child));
                return section;
            case ListBlock list:
                var items = NoteLists.Create(list.IsOrdered ? NoteLists.Kind.Numbers : NoteLists.Kind.Bullets);
                if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start > 0) items.StartIndex = start;
                foreach (ListItemBlock item in list)
                {
                    var row = new ListItem();
                    foreach (var child in item) row.Blocks.Add(RenderBlock(child));
                    items.ListItems.Add(row);
                }
                return items;
            case ThematicBreakBlock:
                var divider = new Paragraph { BorderBrush=new SolidColorBrush(Color.FromArgb(45,0,0,0)), BorderThickness=new Thickness(0,1,0,0), Margin=new Thickness(0,8,0,8) };
                divider.SetValue(ParagraphFormatProperty, BlockStyle.Divider);
                return divider;
            case Markdig.Extensions.Tables.Table table:
                var output = new System.Windows.Documents.Table { CellSpacing=0, Margin=new Thickness(0,4,0,10) };
                var group = new TableRowGroup(); output.RowGroups.Add(group);
                foreach (Markdig.Extensions.Tables.TableRow row in table)
                {
                    var line = new System.Windows.Documents.TableRow(); group.Rows.Add(line);
                    foreach (Markdig.Extensions.Tables.TableCell cell in row)
                    {
                        var column = new System.Windows.Documents.TableCell { Padding=new Thickness(5), BorderBrush=Brushes.Gray, BorderThickness=new Thickness(.5), FontWeight=row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal };
                        foreach (var child in cell) column.Blocks.Add(RenderBlock(child));
                        line.Cells.Add(column);
                    }
                }
                return output;
            case HtmlBlock html:
                var literal = html.Lines.ToString();
                return literal.Trim() == "<br>" ? Paragraph(null) : new Paragraph(new Run(literal));
            default:
                return new Paragraph();
        }
    }
    private static Paragraph Paragraph(ContainerInline inline)
    {
        var paragraph = new Paragraph();
        if (inline != null) AddInlines(paragraph.Inlines, inline);
        return paragraph;
    }
    internal static Run StyledRun(string text, InlineStyle style)
    {
        var run = new Run(text);
        if (style.HasFlag(InlineStyle.Bold)) run.FontWeight = FontWeights.Bold;
        if (style.HasFlag(InlineStyle.Italic)) run.FontStyle = FontStyles.Italic;
        var decorations = new TextDecorationCollection();
        if (style.HasFlag(InlineStyle.Underline)) decorations.Add(TextDecorations.Underline);
        if (style.HasFlag(InlineStyle.Strike)) decorations.Add(TextDecorations.Strikethrough);
        if (decorations.Count > 0) run.TextDecorations = decorations;
        if (style.HasFlag(InlineStyle.Code))
        {
            run.FontFamily = CodeFont;
            run.Background = InlineCodeBackground;
        }
        return run;
    }

    private static void AddInlines(InlineCollection target, ContainerInline container, InlineStyle style = InlineStyle.None)
    {
        var inlineTags = new Stack<(string Close, InlineStyle Before)>();
        var afterTask = false;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline text:
                    var value = text.Content.ToString();
                    if (afterTask && value.Length > 0 && value[0] is ' ' or '\t') value = value[1..];
                    afterTask = false;
                    if (value.Length > 0) target.Add(StyledRun(value, style));
                    break;
                case CodeInline code: target.Add(StyledRun(code.Content, style | InlineStyle.Code)); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    var mark = emphasis.DelimiterChar switch
                    {
                        '~' when emphasis.DelimiterCount >= 2 => InlineStyle.Strike,
                        '+' or '~' => InlineStyle.Underline,
                        '*' or '_' when emphasis.DelimiterCount >= 2 => InlineStyle.Bold,
                        '*' or '_' => InlineStyle.Italic,
                        _ => InlineStyle.None
                    };
                    AddInlines(target, emphasis, style | mark);
                    break;
                case TaskList task:
                    target.Add(TaskMarker(task.Checked));
                    afterTask = true;
                    break;
                case LinkInline link:
                    Span label;
                    if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "mailto"))
                        label = new Hyperlink { NavigateUri = uri };
                    else label = new Span();
                    label.Tag = new LinkInfo(link.Url ?? "", link.IsImage);
                    AddInlines(label.Inlines, link, style);
                    target.Add(label);
                    break;
                case HtmlEntityInline entity:
                    target.Add(StyledRun(entity.Transcoded.ToString(), style));
                    break;
                case HtmlInline html:
                    if (inlineTags.TryPeek(out var open) && html.Tag == open.Close)
                    {
                        style = inlineTags.Pop().Before;
                        break;
                    }
                    var tag = html.Tag switch
                    {
                        "<strong>" => ("</strong>", InlineStyle.Bold),
                        "<em>" => ("</em>", InlineStyle.Italic),
                        "<u>" => ("</u>", InlineStyle.Underline),
                        "<s>" => ("</s>", InlineStyle.Strike),
                        _ => ("", InlineStyle.None)
                    };
                    if (tag.Item2 != InlineStyle.None)
                    {
                        inlineTags.Push((tag.Item1, style));
                        style |= tag.Item2;
                    }
                    else if (html.Tag == "<br>") target.Add(new LineBreak());
                    else target.Add(StyledRun(html.Tag, style));
                    break;
                case AutolinkInline autolink:
                    var address = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
                    if (Uri.TryCreate(address, UriKind.Absolute, out var linkUri) &&
                        linkUri.Scheme is "http" or "https" or "mailto")
                    {
                        target.Add(new Hyperlink(StyledRun(autolink.Url, style)) { NavigateUri = linkUri });
                    }
                    else target.Add(StyledRun(autolink.Url, style));
                    break;
                case ContainerInline child: AddInlines(target, child, style); break;
            }
        }
    }
}
