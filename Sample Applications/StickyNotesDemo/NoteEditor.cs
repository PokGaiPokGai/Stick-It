using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

internal sealed class NoteEditor : RichTextBox
{
    private const string Backtick = "\u0060";
    private const string FormattedNoteClipboard = "StickIt.FormattedNote";
    private static readonly ResourceDictionary TaskStyles = new()
    {
        Source = new Uri("pack://application:,,,/Stick It;component/Assets/TaskStyles.xaml")
    };
    private bool changingDocument;

    internal NoteEditor(string markdown) : base(Render(markdown))
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        AcceptsTab = true;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Resources.MergedDictionaries.Add(TaskStyles);
        TextChanged += (_, e) =>
        {
            DocumentEdited?.Invoke();
            QueueMarkdownInput(e);
        };
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(TaskChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(TaskChanged));
        DataObject.AddCopyingHandler(this, (_, e) => e.DataObject.SetData(FormattedNoteClipboard, "1", false));
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

    private void QueueMarkdownInput(TextChangedEventArgs e)
    {
        if (changingDocument || IsReadOnly || !Selection.IsEmpty || e.UndoAction is UndoAction.Undo or UndoAction.Redo ||
            CaretPosition.Paragraph is not { } paragraph || paragraph.Tag is CodeBlockInfo)
            return;

        var caret = CaretPosition;
        var beforeTrigger = caret.GetNextInsertionPosition(LogicalDirection.Backward);
        if (beforeTrigger?.Paragraph != paragraph) return;
        var input = new TextRange(beforeTrigger, caret).Text;
        if (input == "\u3000") input = " ";
        if (input is not (" " or Backtick)) return;

        var triggerStart = Document.ContentStart.GetOffsetToPosition(beforeTrigger);
        var triggerEnd = Document.ContentStart.GetOffsetToPosition(caret);
        if (!e.Changes.Any(change => change.AddedLength > 0 &&
                change.Offset <= triggerStart && change.Offset + change.AddedLength >= triggerEnd))
            return;

        var prefix = new TextRange(paragraph.ContentStart, beforeTrigger).Text;
        if (input == " " && QueueBlockShortcut(paragraph, prefix, caret)) return;
        var candidate = input == Backtick ? prefix + input : prefix;
        if (candidate.Length == 0 || candidate[^1] is not ('*' or '_' or '~' or '+' or (char)0x60 or ')' or '>') ||
            ReadInlineShortcut(candidate, input == Backtick) is not { } shortcut)
            return;

        var start = MoveBackByText(beforeTrigger, prefix.Length - shortcut.Start);
        if (start == null) return;
        ApplyAfterInput(start, caret, range =>
        {
            range.Text = "";
            if (input == " ") shortcut.Content.Inlines.Add(new Run(" "));
            InsertInlines(shortcut.Content, range.Start);
            if (input == Backtick)
            {
                Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, Document.FontFamily);
                Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
        });
    }

    private bool QueueBlockShortcut(Paragraph paragraph, string prefix, TextPointer end)
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

        ApplyAfterInput(paragraph.ContentStart, end, marker =>
        {
            marker.Text = "";
            CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            format();
        });
        return true;
    }

    private void ApplyAfterInput(TextPointer start, TextPointer end, Action<TextRange> apply)
    {
        var document = Document;
        start = start.GetPositionAtOffset(0, LogicalDirection.Forward);
        end = end.GetPositionAtOffset(0, LogicalDirection.Backward);
        var original = new TextRange(start, end).Text;

        // WPF batches keystrokes. Wait for the native edit to finish, and keep
        // this range anchored before any characters typed after the shortcut.
        Dispatcher.InvokeAsync(() =>
        {
            if (IsReadOnly || !ReferenceEquals(Document, document) || start.CompareTo(end) >= 0) return;
            var range = new TextRange(start, end);
            if (range.Text != original) return;
            var caretAtTrigger = Selection.IsEmpty && CaretPosition.CompareTo(end) == 0;
            var selectionStart = Selection.Start.GetPositionAtOffset(0, LogicalDirection.Forward);
            var selectionEnd = Selection.End.GetPositionAtOffset(0, LogicalDirection.Forward);
            Change(() =>
            {
                apply(range);
                if (!caretAtTrigger) Selection.Select(selectionStart, selectionEnd);
            });
        }, DispatcherPriority.Input);
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
        if (e.Handled || IsReadOnly || e.Key != Key.Enter || e.KeyboardDevice.Modifiers != ModifierKeys.None ||
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
        var clipboard = e.SourceDataObject;
        if (e.IsDragDrop || clipboard.GetDataPresent(FormattedNoteClipboard, false)) return;
        var text = clipboard.GetData(DataFormats.UnicodeText) as string ?? clipboard.GetData(DataFormats.Text) as string;
        if (text == null) return;
        var plainText = e.FormatToApply == DataFormats.UnicodeText || e.FormatToApply == DataFormats.Text;
        var fragment = ReadFormattedPaste(text, allowIndentedCode: plainText);
        if (fragment == null) return;
        Change(() => InsertFragment(fragment));
        e.CancelCommand();
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
        changingDocument = true;
        try { action(); }
        finally
        {
            EndChange();
            changingDocument = false;
        }
        DocumentEdited?.Invoke();
    }
}
