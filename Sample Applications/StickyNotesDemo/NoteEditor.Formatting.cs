using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static StickyNotesDemo.MarkdownView;
using ListKind = StickyNotesDemo.NoteLists.Kind;

namespace StickyNotesDemo;

internal sealed partial class NoteEditor
{
    internal readonly record struct FormattingState(
        bool? Heading, bool? Bold, bool? Italic, bool? Underline, bool? Strike,
        bool? Bullets, bool? Numbers, bool? Tasks, bool? Code);

    internal FormattingState ReadFormatting()
    {
        var marks = FormattingRanges().Select(ReadMarks).ToArray();
        var paragraphs = SelectedParagraphs().ToArray();
        return new FormattingState(
            Coverage(paragraphs, paragraph => HeadingLevel(paragraph) > 0),
            Coverage(marks, mark => mark.HasFlag(InlineStyle.Bold)),
            Coverage(marks, mark => mark.HasFlag(InlineStyle.Italic)),
            Coverage(marks, mark => mark.HasFlag(InlineStyle.Underline)),
            Coverage(marks, mark => mark.HasFlag(InlineStyle.Strike)),
            Coverage(paragraphs, paragraph => ListFormat(paragraph) == ListKind.Bullets),
            Coverage(paragraphs, paragraph => ListFormat(paragraph) == ListKind.Numbers),
            Coverage(paragraphs, paragraph => ListFormat(paragraph) == ListKind.Tasks),
            Coverage(marks, mark => mark.HasFlag(InlineStyle.Code)));
    }

    private static ListKind? ListFormat(Paragraph paragraph)
        => paragraph.Parent is ListItem { Parent: List list } ? NoteLists.KindOf(list) : null;

    private static bool? Coverage<T>(T[] values, Func<T, bool> isActive)
    {
        var active = values.Count(isActive);
        return active == 0 ? false : active == values.Length ? true : null;
    }

    internal void ToggleMark(InlineStyle style)
    {
        CommitTyping();
        var ranges = FormattingRanges().ToArray();
        var remove = ranges.Length > 0 && ranges.All(range => ReadMarks(range).HasFlag(style));
        Change(() =>
        {
            switch (style)
            {
                case InlineStyle.Bold:
                    ToggleCharacterProperty(EditingCommands.ToggleBold, TextElement.FontWeightProperty,
                        FontWeights.Bold, FontWeights.Normal, remove);
                    break;
                case InlineStyle.Italic:
                    ToggleCharacterProperty(EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
                        FontStyles.Italic, FontStyles.Normal, remove);
                    break;
                case InlineStyle.Underline:
                case InlineStyle.Strike:
                    var decoration = style == InlineStyle.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough;
                    var location = decoration[0].Location;
                    foreach (var range in ranges)
                    {
                        var previous = range.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
                        var updated = new TextDecorationCollection(previous?.Where(item => item.Location != location) ?? Enumerable.Empty<TextDecoration>());
                        if (!remove) updated.Add(decoration);
                        range.ApplyPropertyValue(Inline.TextDecorationsProperty, updated);
                    }
                    break;
                case InlineStyle.Code:
                    Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, remove ? Document.FontFamily : CodeFont);
                    Selection.ApplyPropertyValue(TextElement.BackgroundProperty, remove ? Brushes.Transparent : InlineCodeBackground);
                    break;
            }
        });
    }

    private void ToggleCharacterProperty(RoutedCommand command, DependencyProperty property,
        object activeValue, object inactiveValue, bool remove)
    {
        // Keep WPF's caret and typing behavior for its native Bold and Italic values.
        var nativeRemoves = Equals(Selection.GetPropertyValue(property), activeValue);
        if (nativeRemoves == remove) RunNative(command);
        else Selection.ApplyPropertyValue(property, remove ? inactiveValue : activeValue);
    }

    private static InlineStyle ReadMarks(TextRange range)
    {
        var marks = InlineStyle.None;
        if (range.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight &&
            weight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight())
            marks |= InlineStyle.Bold;
        if (range.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style && style != FontStyles.Normal)
            marks |= InlineStyle.Italic;
        if (range.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
        {
            if (decorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline))
                marks |= InlineStyle.Underline;
            if (decorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough))
                marks |= InlineStyle.Strike;
        }
        if (range.Start.Paragraph is { } paragraph && FormatOf(paragraph) is not CodeBlockInfo &&
            range.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily font && font.Source == CodeFont.Source)
            marks |= InlineStyle.Code;
        return marks;
    }

    private IEnumerable<TextRange> FormattingRanges()
    {
        if (Selection.IsEmpty)
        {
            // The selection also exposes formatting applied to the next keystroke.
            yield return Selection;
            yield break;
        }

        var end = Selection.End;
        for (var point = Selection.Start; point.CompareTo(end) < 0;)
        {
            var next = point.GetNextContextPosition(LogicalDirection.Forward);
            if (next.CompareTo(end) > 0) next = end;
            if (point.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                yield return new TextRange(point, next);
            point = next;
        }
    }
}
