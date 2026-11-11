using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

internal sealed class NoteEditor : RichTextBox
{
    private const string Backtick = "\u0060";

    internal NoteEditor(string markdown) : base(Render(markdown))
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        AcceptsTab = true;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        TextChanged += (_, _) => DocumentEdited?.Invoke();
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(TaskChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(TaskChanged));
        DataObject.AddPastingHandler(this, Paste);
    }

    internal event Action DocumentEdited;
    internal string PlainText => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');
    internal string Markdown => MarkdownWriter.Write(Document);

    private void TaskChanged(object sender, RoutedEventArgs e) => DocumentEdited?.Invoke();

    internal void ToggleHeading()
    {
        var paragraphs = SelectedParagraphs().Where(p => p.Tag is not CodeBlockInfo and not BlockStyle.Divider).ToArray();
        var level = paragraphs.All(p => HeadingLevel(p) > 0) ? 0 : 2;
        Change(() =>
        {
            foreach (var paragraph in paragraphs) SetHeading(paragraph, level);
        });
    }

    internal void ToggleList(bool ordered)
        => Change(() => (ordered ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets).Execute(null, this));

    internal void ToggleTask()
    {
        var paragraphs = SelectedParagraphs().Where(p => p.Tag is not CodeBlockInfo and not BlockStyle.Divider).ToArray();
        Change(() =>
        {
            foreach (var paragraph in paragraphs)
            {
                var marker = paragraph.Inlines.OfType<InlineUIContainer>().FirstOrDefault(inline => inline.Child is CheckBox);
                if (marker?.Child is CheckBox check) check.IsChecked = check.IsChecked != true;
                else InsertTask(paragraph, false);
            }
        });
    }

    internal void ToggleMark(InlineStyle style)
    {
        Change(() =>
        {
            switch (style)
            {
                case InlineStyle.Bold:
                    EditingCommands.ToggleBold.Execute(null, this);
                    break;
                case InlineStyle.Italic:
                    EditingCommands.ToggleItalic.Execute(null, this);
                    break;
                case InlineStyle.Underline:
                case InlineStyle.Strike:
                    var decoration = style == InlineStyle.Underline ? TextDecorations.Underline : TextDecorations.Strikethrough;
                    var location = decoration[0].Location;
                    var remove = Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection current
                        && current.Any(item => item.Location == location);
                    var ranges = Selection.IsEmpty ? [Selection] : SelectedTextRuns().ToArray();
                    foreach (var range in ranges)
                    {
                        var previous = range.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
                        var updated = new TextDecorationCollection(previous?.Where(item => item.Location != location) ?? Enumerable.Empty<TextDecoration>());
                        if (!remove) updated.Add(decoration);
                        range.ApplyPropertyValue(Inline.TextDecorationsProperty, updated);
                    }
                    break;
                case InlineStyle.Code:
                    var isCode = Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily font && font.Source == CodeFont.Source;
                    Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, isCode ? Document.FontFamily : CodeFont);
                    Selection.ApplyPropertyValue(TextElement.BackgroundProperty, isCode ? Brushes.Transparent : InlineCodeBackground);
                    break;
            }
        });
    }

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        var found = new HashSet<Paragraph>();
        for (var point = Selection.Start; point != null && point.CompareTo(Selection.End) <= 0;
             point = point.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (point.Paragraph is { } paragraph && found.Add(paragraph)) yield return paragraph;
        }
    }

    private IEnumerable<TextRange> SelectedTextRuns()
    {
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

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (!e.Handled && ApplyMarkdownInput(e.Text)) e.Handled = true;
    }

    internal bool ApplyMarkdownInput(string input)
    {
        if (IsReadOnly || !Selection.IsEmpty || input is not (" " or Backtick) ||
            CaretPosition.Paragraph is not { } paragraph || paragraph.Tag is CodeBlockInfo)
            return false;

        var prefix = new TextRange(paragraph.ContentStart, CaretPosition).Text;
        if (input == " " && ApplyBlockShortcut(paragraph, prefix))
            return true;

        var candidate = input == Backtick ? prefix + input : prefix;
        if (candidate.Length == 0 || candidate[^1] is not ('*' or '_' or '~' or '+' or (char)0x60 or ')' or '>') ||
            ReadInlineShortcut(candidate, input == Backtick) is not { } shortcut)
            return false;

        var start = MoveBackByText(CaretPosition, prefix.Length - shortcut.Start);
        if (start == null) return false;
        Change(() =>
        {
            var range = new TextRange(start, CaretPosition);
            range.Text = "";
            if (input == " ") shortcut.Content.Inlines.Add(new Run(" "));
            InsertInlines(shortcut.Content, range.Start);
            if (input == Backtick)
            {
                Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, Document.FontFamily);
                Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
        });
        return true;
    }

    private bool ApplyBlockShortcut(Paragraph paragraph, string prefix)
    {
        Action format;
        if (prefix.Length is >= 1 and <= 6 && prefix.All(character => character == '#'))
            format = () => SetHeading(paragraph, prefix.Length);
        else if ((prefix is "-" or "*") && paragraph.Parent is not ListItem)
            format = () => EditingCommands.ToggleBullets.Execute(null, this);
        else if (prefix == "1." && paragraph.Parent is not ListItem)
            format = () => EditingCommands.ToggleNumbering.Execute(null, this);
        else if (prefix is "[]" or "[ ]" or "[x]" or "- [ ]" or "- [x]")
            format = () => InsertTask(paragraph, prefix.Contains('x'));
        else if (prefix == ">")
            format = () => WrapQuote(paragraph);
        else if (prefix == Backtick + Backtick + Backtick)
            format = () => SetCodeBlock(paragraph, "");
        else return false;

        Change(() =>
        {
            var marker = new TextRange(paragraph.ContentStart, CaretPosition);
            marker.Text = "";
            CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            format();
        });
        return true;
    }

    private void InsertTask(Paragraph paragraph, bool isChecked)
    {
        var marker = TaskMarker(isChecked);
        if (paragraph.Inlines.FirstInline is { } first) paragraph.Inlines.InsertBefore(first, marker);
        else paragraph.Inlines.Add(marker);
        if (paragraph.Parent is ListItem { Parent: List list } && list.ListItems.All(item =>
                item.Blocks.FirstBlock is Paragraph line && line.Inlines.OfType<InlineUIContainer>().Any(inline => inline.Child is CheckBox)))
        {
            list.MarkerStyle = TextMarkerStyle.None;
            list.Padding = new Thickness(0);
        }
        if (Selection.IsEmpty) CaretPosition = marker.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
    }

    private void WrapQuote(Paragraph paragraph)
    {
        var blocks = ContainingBlocks(paragraph);
        var quote = Quote();
        blocks.InsertBefore(paragraph, quote);
        blocks.Remove(paragraph);
        quote.Blocks.Add(paragraph);
        CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || IsReadOnly || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None ||
            !Selection.IsEmpty || CaretPosition.Paragraph is not { } paragraph) return;

        if (paragraph.Tag is CodeBlockInfo)
        {
            EditingCommands.EnterLineBreak.Execute(null, this);
            e.Handled = true;
        }
        else if (HeadingLevel(paragraph) > 0 &&
                 new TextRange(CaretPosition, paragraph.ContentEnd).Text.Length == 0)
        {
            Change(() =>
            {
                var next = CaretPosition.InsertParagraphBreak();
                SetHeading(next.Paragraph, 0);
                CaretPosition = next;
            });
            e.Handled = true;
        }
    }

    private void Paste(object sender, DataObjectPastingEventArgs e)
    {
        if ((e.FormatToApply == DataFormats.UnicodeText || e.FormatToApply == DataFormats.Text) &&
            e.SourceDataObject.GetData(DataFormats.UnicodeText) is string text && PasteMarkdown(text))
            e.CancelCommand();
    }

    internal bool PasteMarkdown(string text)
    {
        var fragment = ReadFormattedPaste(text);
        if (fragment == null) return false;
        Change(() => InsertFragment(fragment));
        return true;
    }

    private void InsertFragment(FlowDocument fragment)
    {
        if (!Selection.IsEmpty) Selection.Text = "";
        var caret = CaretPosition;
        var paragraph = caret.Paragraph;
        if (fragment.Blocks.Count == 1 && fragment.Blocks.FirstBlock is Paragraph source && source.Tag == null)
        {
            InsertInlines(source, caret);
            return;
        }

        var before = new TextRange(paragraph.ContentStart, caret).Text.Length > 0;
        var after = new TextRange(caret, paragraph.ContentEnd).Text.Length > 0;
        var replace = !before && !after && paragraph.Tag == null && !paragraph.Inlines.OfType<InlineUIContainer>().Any();
        var anchor = before && after ? caret.InsertParagraphBreak().Paragraph
            : before ? paragraph.NextBlock : paragraph;
        var blocks = ContainingBlocks(anchor ?? paragraph);
        Block last = null;
        foreach (var block in fragment.Blocks.ToArray())
        {
            fragment.Blocks.Remove(block);
            if (anchor == null) blocks.Add(block);
            else blocks.InsertBefore(anchor, block);
            last = block;
        }
        if (replace) blocks.Remove(paragraph);
        CaretPosition = last.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
    }

    private void InsertInlines(Paragraph source, TextPointer caret)
    {
        var span = new Span((Inline)null, caret);
        foreach (var inline in source.Inlines.ToArray())
        {
            source.Inlines.Remove(inline);
            span.Inlines.Add(inline);
        }
        CaretPosition = span.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
    }

    private static BlockCollection ContainingBlocks(Block block) => block.Parent switch
    {
        FlowDocument document => document.Blocks,
        Section section => section.Blocks,
        ListItem item => item.Blocks,
        TableCell cell => cell.Blocks,
        _ => throw new InvalidOperationException("Unsupported note block container.")
    };

    private static TextPointer MoveBackByText(TextPointer point, int length)
    {
        while (length > 0 && point != null)
        {
            var runLength = point.GetTextRunLength(LogicalDirection.Backward);
            if (runLength >= length) return point.GetPositionAtOffset(-length, LogicalDirection.Backward);
            length -= runLength;
            point = point.GetNextContextPosition(LogicalDirection.Backward);
        }
        return point;
    }

    private void Change(Action action)
    {
        BeginChange();
        try { action(); }
        finally { EndChange(); }
        DocumentEdited?.Invoke();
    }
}
