using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace StickyNotesDemo;

internal static class NoteLists
{
    internal enum Kind { Bullets, Numbers, Tasks }

    internal const double TaskWidth = 20;
    internal const double TaskGap = 5;
    private const double TextInset = 26;
    private static readonly Thickness BodyMargin = new(0, 0, 0, 8);
    private static readonly Thickness ItemMargin = new(0, 0, 0, 4);
    private static readonly Thickness ListPadding = new(TextInset, 0, 0, 0);

    internal static Kind KindOf(List list) => list.MarkerStyle switch
    {
        TextMarkerStyle.Decimal or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
            or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman => Kind.Numbers,
        TextMarkerStyle.None => Kind.Tasks,
        _ => Kind.Bullets
    };

    internal static List Create(Kind kind, int startIndex = 1) => new()
    {
        MarkerStyle = Marker(kind), StartIndex = startIndex,
        Margin = new Thickness(0), Padding = ListPadding, MarkerOffset = 4
    };

    private static TextMarkerStyle Marker(Kind kind) => kind switch
    {
        Kind.Numbers => TextMarkerStyle.Decimal,
        Kind.Tasks => TextMarkerStyle.None,
        _ => TextMarkerStyle.Disc
    };

    internal static InlineUIContainer TaskOf(Paragraph paragraph) => TaskMarkers(paragraph.Inlines).FirstOrDefault();

    private static IEnumerable<InlineUIContainer> TaskMarkers(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is InlineUIContainer { Child: CheckBox } marker) yield return marker;
            else if (inline is Span span)
                foreach (var nested in TaskMarkers(span.Inlines)) yield return nested;
        }
    }

    internal static void SetTask(Paragraph paragraph, bool task)
    {
        var markers = TaskMarkers(paragraph.Inlines).ToArray();
        if (task && markers.Length == 0)
            InsertTask(paragraph, MarkdownView.TaskMarker(false));
        foreach (var marker in markers.Skip(task ? 1 : 0))
            RemoveTask(paragraph, marker);
        if (task && markers.Length != 0 && markers[0] != paragraph.Inlines.FirstInline)
        {
            RemoveTask(paragraph, markers[0]);
            InsertTask(paragraph, markers[0]);
        }
    }

    internal static void ContinueTask(Paragraph previous, Paragraph next, InlineUIContainer marker, bool atStart)
    {
        if (atStart)
        {
            RemoveTask(previous, marker);
            InsertTask(next, marker);
            SetTask(previous, true);
        }
        else SetTask(next, true);
    }

    private static void RemoveTask(Paragraph paragraph, InlineUIContainer marker)
        => (marker.Parent is Span span ? span.Inlines : paragraph.Inlines).Remove(marker);

    private static void InsertTask(Paragraph paragraph, InlineUIContainer marker)
    {
        if (paragraph.Inlines.FirstInline is { } first) paragraph.Inlines.InsertBefore(first, marker);
        else paragraph.Inlines.Add(marker);
    }

    internal static TextPointer TextStart(Paragraph paragraph)
        => (TaskOf(paragraph)?.ElementEnd ?? paragraph.ContentStart).GetInsertionPosition(LogicalDirection.Forward);

    internal static bool IsEmpty(Paragraph paragraph)
        => string.IsNullOrWhiteSpace(InlineText(paragraph.ContentStart, paragraph.ContentEnd));

    // TextRange.Text includes WPF's generated list marker for a whole row.
    // Editing decisions need the actual inline content of the paragraph.
    internal static string InlineText(TextPointer start, TextPointer end)
    {
        var text = new StringBuilder();
        for (var point = start; point.CompareTo(end) < 0; point = point.GetNextContextPosition(LogicalDirection.Forward))
        {
            var context = point.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var run = point.GetTextInRun(LogicalDirection.Forward);
                text.Append(run, 0, Math.Min(run.Length, point.GetOffsetToPosition(end)));
            }
            else if (context == TextPointerContext.ElementStart &&
                     point.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                text.Append('\n');
        }
        return text.ToString();
    }

    internal static BlockCollection BlocksOf(Block block) => block.Parent switch
    {
        FlowDocument document => document.Blocks,
        Section section => section.Blocks,
        ListItem item => item.Blocks,
        TableCell cell => cell.Blocks,
        Floater floater => floater.Blocks,
        Figure figure => figure.Blocks,
        _ => throw new InvalidOperationException("Unsupported note block container.")
    };

    internal static void Wrap(Paragraph paragraph, Kind kind, int? startIndex = null)
    {
        var blocks = BlocksOf(paragraph);
        var list = Create(kind, startIndex ?? NextNumber(paragraph.PreviousBlock, kind));
        blocks.InsertBefore(paragraph, list);
        blocks.Remove(paragraph);
        SetTask(paragraph, kind == Kind.Tasks);
        list.ListItems.Add(new ListItem(paragraph));
    }

    internal static void ChangeItems(List list, HashSet<ListItem> selected, Kind? kind, int? startIndex = null)
    {
        var original = KindOf(list);
        if (kind == original) return;
        if (kind is { } applied && list.ListItems.All(selected.Contains))
        {
            list.MarkerStyle = Marker(applied);
            if (applied == Kind.Numbers)
                list.StartIndex = startIndex ?? NextNumber(list.PreviousBlock, applied);
            foreach (var item in list.ListItems.ToArray())
                if (item.Blocks.FirstBlock is Paragraph line) SetTask(line, applied == Kind.Tasks);
            return;
        }

        var rows = list.ListItems.ToArray().Select((item, index) =>
        {
            var next = selected.Contains(item) ? kind : original;
            if (selected.Contains(item) && item.Blocks.FirstBlock is Paragraph line)
                SetTask(line, next == Kind.Tasks);
            var number = next != Kind.Numbers ? null : original == Kind.Numbers ? list.StartIndex + index : startIndex;
            return new Row(item, next, number);
        }).ToArray();
        ReplaceWithRuns(list, rows);
    }

    private sealed record Row(ListItem Item, Kind? Kind, int? Number);

    private static void ReplaceWithRuns(List list, Row[] rows)
    {
        var blocks = BlocksOf(list);
        for (var i = 0; i < rows.Length;)
        {
            var row = rows[i];
            if (row.Kind is not { } kind)
            {
                foreach (var block in row.Item.Blocks.ToArray())
                {
                    row.Item.Blocks.Remove(block);
                    blocks.InsertBefore(list, block);
                }
                i++;
                continue;
            }

            var run = Create(kind, row.Number ?? NextNumber(list.PreviousBlock, kind));
            blocks.InsertBefore(list, run);
            do
            {
                var item = rows[i++].Item;
                list.ListItems.Remove(item);
                run.ListItems.Add(item);
            } while (i < rows.Length && rows[i].Kind == kind);
        }
        blocks.Remove(list);
    }

    private static int NextNumber(Block previous, Kind kind)
        => kind == Kind.Numbers && previous is List list && KindOf(list) == kind
            ? list.StartIndex + list.ListItems.Count : 1;

    internal static ListItem[] OutermostItems(IEnumerable<ListItem> items)
    {
        var selected = items.ToHashSet();
        return items.Where(item =>
        {
            for (var parent = ((List)item.Parent).Parent as ListItem; parent != null;
                 parent = ((List)parent.Parent).Parent as ListItem)
                if (selected.Contains(parent)) return false;
            return true;
        }).ToArray();
    }

    internal static void Indent(ListItem[] items)
    {
        var selected = items.ToHashSet();
        var parents = new Dictionary<ListItem, ListItem>();
        foreach (var item in items)
        {
            var list = (List)item.Parent;
            var previous = item.PreviousListItem ?? (list.PreviousBlock as List)?.ListItems.LastListItem;
            if (previous == null) continue;
            var parent = selected.Contains(previous) ? parents.GetValueOrDefault(previous) : previous;
            if (parent != null) parents.Add(item, parent);
        }

        foreach (var item in items)
        {
            if (!parents.TryGetValue(item, out var parent)) continue;
            var list = (List)item.Parent;
            var kind = KindOf(list);
            var target = parent.Blocks.LastBlock as List;
            if (target == null || KindOf(target) != kind)
            {
                target = Create(kind);
                parent.Blocks.Add(target);
            }
            list.ListItems.Remove(item);
            target.ListItems.Add(item);
        }
    }

    internal static void Normalize(FlowDocument document) => NormalizeBlocks(document.Blocks, inList: false);

    private static void NormalizeBlocks(BlockCollection blocks, bool inList)
    {
        // A pasted Markdown list can alternate checkboxes and bullets. Each
        // run gets its own marker style, while all rows share the same inset.
        foreach (var list in blocks.OfType<List>().ToArray())
        {
            if (list.ListItems.Count == 0) { blocks.Remove(list); continue; }
            var original = KindOf(list);
            var rows = list.ListItems.Select((item, index) =>
            {
                var task = item.Blocks.FirstBlock is Paragraph line && TaskOf(line) != null;
                var kind = task ? Kind.Tasks : original == Kind.Tasks ? Kind.Bullets : original;
                return new Row(item, kind, original == Kind.Numbers ? list.StartIndex + index : null);
            }).ToArray();
            if (rows.Any(row => row.Kind != rows[0].Kind))
                ReplaceWithRuns(list, rows);
            else
                list.MarkerStyle = Marker(rows[0].Kind.Value);
        }

        List previous = null;
        foreach (var block in blocks.ToArray())
        {
            if (block is List list)
            {
                var kind = KindOf(list);
                if (previous != null && KindOf(previous) == kind &&
                    (kind != Kind.Numbers || list.StartIndex == NextNumber(previous, kind)))
                {
                    foreach (var item in list.ListItems.ToArray())
                    {
                        list.ListItems.Remove(item);
                        previous.ListItems.Add(item);
                    }
                    blocks.Remove(list);
                    list = previous;
                }
                list.Margin = new Thickness(0);
                list.Padding = ListPadding;
                list.MarkerOffset = 4;
                foreach (var item in list.ListItems.ToArray())
                {
                    if (item.Blocks.FirstBlock is Paragraph line) SetTask(line, kind == Kind.Tasks);
                    item.Margin = new Thickness(0);
                    item.Padding = new Thickness(0);
                    NormalizeBlocks(item.Blocks, inList: true);
                }
                previous = list;
                continue;
            }

            previous = null;
            switch (block)
            {
                case Paragraph paragraph when MarkdownView.FormatOf(paragraph) is not (MarkdownView.CodeBlockInfo or MarkdownView.BlockStyle.Divider):
                    paragraph.Margin = inList ? ItemMargin : BodyMargin;
                    paragraph.Padding = new Thickness(0);
                    var task = paragraph.Parent is ListItem item && item.Blocks.FirstBlock == paragraph &&
                               TaskOf(paragraph) != null;
                    if (!task) SetTask(paragraph, false);
                    if (inList || paragraph.TextIndent < 0)
                        paragraph.TextIndent = task ? -(TaskWidth + TaskGap) : 0;
                    break;
                case Section section:
                    NormalizeBlocks(section.Blocks, inList);
                    break;
                case Table table:
                    foreach (var cell in table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells).ToArray())
                        NormalizeBlocks(cell.Blocks, inList: false);
                    break;
            }
        }
    }
}
