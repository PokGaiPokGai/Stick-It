using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace StickyNotesDemo;

internal sealed class NotePreview
{
    private static readonly ResourceDictionary TaskStyles = new()
    {
        Source = new Uri("pack://application:,,,/Stick It;component/Assets/TaskStyles.xaml")
    };
    private int remainingCharacters = 640;
    private int remainingParagraphs = 6;
    private bool truncated;
    private Paragraph lastParagraph;

    internal static FlowDocument Create(FlowDocument source)
    {
        var preview = new NotePreview();
        var document = new FlowDocument
        {
            FontFamily = source.FontFamily,
            FontSize = 14,
            Foreground = source.Foreground,
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity
        };
        document.Resources.MergedDictionaries.Add(TaskStyles);
        preview.CopyBlocks(source.Blocks, document.Blocks);
        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph(new Run("空白便签")) { Margin = new Thickness(0) });
        else if (document.Blocks.Count == 1 && document.Blocks.FirstBlock is Paragraph { Inlines.Count: 0 } empty &&
                 empty.BorderThickness == new Thickness(0))
            empty.Inlines.Add(new Run("空白便签"));
        if (preview.truncated && preview.lastParagraph != null)
            preview.lastParagraph.Inlines.Add(new Run("…"));
        return document;
    }

    private bool Full => remainingCharacters == 0 || remainingParagraphs == 0;

    private void CopyBlocks(BlockCollection source, BlockCollection target)
    {
        foreach (var block in source)
        {
            if (Full) { truncated = true; break; }
            switch (block)
            {
                case Paragraph paragraph:
                    var copy = new Paragraph
                    {
                        FontWeight = paragraph.FontWeight,
                        FontFamily = paragraph.FontFamily,
                        TextDecorations = paragraph.TextDecorations,
                        Background = paragraph.Background,
                        BorderBrush = paragraph.BorderBrush,
                        BorderThickness = paragraph.BorderThickness,
                        Padding = paragraph.Padding,
                        Margin = new Thickness(0),
                        LineHeight = 20,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight
                    };
                    target.Add(copy);
                    remainingParagraphs--;
                    CopyInlines(paragraph.Inlines, copy.Inlines);
                    lastParagraph = copy;
                    break;
                case List list:
                    var listCopy = new List
                    {
                        MarkerStyle = list.MarkerStyle,
                        StartIndex = list.StartIndex,
                        MarkerOffset = list.MarkerOffset,
                        Margin = new Thickness(0),
                        Padding = list.Padding
                    };
                    target.Add(listCopy);
                    foreach (var item in list.ListItems)
                    {
                        if (Full) { truncated = true; break; }
                        var itemCopy = new ListItem { Margin = new Thickness(0), Padding = new Thickness(0) };
                        listCopy.ListItems.Add(itemCopy);
                        CopyBlocks(item.Blocks, itemCopy.Blocks);
                    }
                    break;
                case Section section:
                    var sectionCopy = new Section
                    {
                        BorderBrush = section.BorderBrush,
                        BorderThickness = section.BorderThickness,
                        Padding = section.Padding,
                        Margin = new Thickness(0)
                    };
                    target.Add(sectionCopy);
                    CopyBlocks(section.Blocks, sectionCopy.Blocks);
                    break;
                case Table table:
                    var tableCopy = new Table { CellSpacing = 0, Margin = new Thickness(0) };
                    target.Add(tableCopy);
                    foreach (var group in table.RowGroups)
                    {
                        if (Full) { truncated = true; break; }
                        var groupCopy = new TableRowGroup();
                        tableCopy.RowGroups.Add(groupCopy);
                        foreach (var row in group.Rows)
                        {
                            if (Full) { truncated = true; break; }
                            var rowCopy = new TableRow();
                            groupCopy.Rows.Add(rowCopy);
                            foreach (var cell in row.Cells)
                            {
                                if (Full) { truncated = true; break; }
                                var cellCopy = new TableCell
                                {
                                    BorderBrush = cell.BorderBrush,
                                    BorderThickness = cell.BorderThickness,
                                    Padding = new Thickness(3),
                                    FontWeight = cell.FontWeight
                                };
                                rowCopy.Cells.Add(cellCopy);
                                CopyBlocks(cell.Blocks, cellCopy.Blocks);
                            }
                        }
                    }
                    break;
            }
        }
    }

    private void CopyInlines(InlineCollection source, InlineCollection target)
    {
        foreach (var inline in source)
        {
            if (remainingCharacters == 0) { truncated = true; break; }
            Inline copy;
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length == 0) continue;
                    var length = Math.Min(run.Text.Length, remainingCharacters);
                    if (length < run.Text.Length && length > 0 && char.IsHighSurrogate(run.Text[length - 1])) length--;
                    copy = new Run(run.Text[..length]);
                    remainingCharacters -= length;
                    if (length < run.Text.Length) { truncated = true; remainingCharacters = 0; }
                    break;
                case LineBreak:
                    copy = new LineBreak();
                    remainingCharacters--;
                    break;
                case Span span:
                    var spanCopy = new Span();
                    CopyInlines(span.Inlines, spanCopy.Inlines);
                    copy = spanCopy;
                    break;
                case InlineUIContainer { Child: CheckBox checkbox }:
                    copy = MarkdownView.TaskMarker(checkbox.IsChecked == true);
                    remainingCharacters--;
                    break;
                default:
                    continue;
            }
            copy.FontWeight = inline.FontWeight;
            copy.FontStyle = inline.FontStyle;
            copy.FontFamily = inline.FontFamily;
            copy.TextDecorations = inline.TextDecorations;
            copy.Foreground = inline.Foreground;
            copy.Background = inline.Background;
            target.Add(copy);
        }
    }
}
