using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.TaskLists;

namespace StickyNotesDemo;

public static class MarkdownView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().UseEmphasisExtras().Build();
    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize=14,
            Foreground=new SolidColorBrush(Color.FromRgb(38,38,38)), PagePadding=new Thickness(0),
            ColumnWidth=double.PositiveInfinity };
        foreach (var block in Markdig.Markdown.Parse(markdown, Pipeline)) document.Blocks.Add(RenderBlock(block));
        return document;
    }
    private static System.Windows.Documents.Block RenderBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Paragraph(heading.Inline);
                title.FontSize = new double[] { 26,22,19,17,15,14 }[heading.Level-1];
                title.FontWeight = FontWeights.SemiBold;
                return title;
            case ParagraphBlock paragraph: return Paragraph(paragraph.Inline);
            case CodeBlock code:
                return new Paragraph(new Run(code.Lines.ToString())) { FontFamily=new FontFamily("Consolas"), FontSize=12,
                    Background=new SolidColorBrush(Color.FromArgb(14,0,0,0)), Padding=new Thickness(8), Margin=new Thickness(0,4,0,10) };
            case QuoteBlock quote:
                var section = new Section { BorderBrush=new SolidColorBrush(Color.FromArgb(70,0,0,0)),
                    BorderThickness=new Thickness(3,0,0,0), Padding=new Thickness(10,0,0,0), Margin=new Thickness(0,4,0,10) };
                foreach (var child in quote) section.Blocks.Add(RenderBlock(child));
                return section;
            case ListBlock list:
                var items = new System.Windows.Documents.List { MarkerStyle=list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Padding=new Thickness(22,0,0,0), Margin=new Thickness(0,0,0,8) };
                foreach (ListItemBlock item in list)
                {
                    var row = new ListItem();
                    foreach (var child in item) row.Blocks.Add(RenderBlock(child));
                    items.ListItems.Add(row);
                }
                return items;
            case ThematicBreakBlock:
                return new Paragraph { BorderBrush=new SolidColorBrush(Color.FromArgb(45,0,0,0)), BorderThickness=new Thickness(0,1,0,0), Margin=new Thickness(0,8,0,8) };
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
            default:
                return new Paragraph();
        }
    }
    private static Paragraph Paragraph(ContainerInline inline)
    {
        var paragraph = new Paragraph { Margin=new Thickness(0,0,0,8) };
        if (inline != null) AddInlines(paragraph.Inlines, inline);
        return paragraph;
    }
    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline text: target.Add(new Run(text.Content.ToString())); break;
                case CodeInline code: target.Add(new Run(code.Content) { FontFamily=new FontFamily("Consolas"), Background=new SolidColorBrush(Color.FromArgb(18,0,0,0)) }); break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterChar == '~' ? new Span { TextDecorations=TextDecorations.Strikethrough }
                        : emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis); target.Add(span); break;
                case TaskList task: target.Add(new Run(task.Checked ? "☑ " : "☐ ")); break;
                case LinkInline link:
                    var label = new Span(); AddInlines(label.Inlines, link);
                    if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http" || uri.Scheme == "mailto"))
                    {
                        var hyperlink = new Hyperlink(label) { NavigateUri=uri };
                        hyperlink.RequestNavigate += (_, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute=true }); e.Handled=true; };
                        target.Add(hyperlink);
                    }
                    else target.Add(label);
                    break;
                case ContainerInline child: AddInlines(target, child); break;
            }
        }
    }
}
