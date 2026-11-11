using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

internal sealed partial class NoteEditor : RichTextBox
{
    private const string Backtick = "\u0060";
    private const string FormattedNoteClipboard = "StickIt.FormattedNote";
    private static readonly ResourceDictionary TaskStyles = new()
    {
        Source = new Uri("pack://application:,,,/Stick It;component/Assets/TaskStyles.xaml")
    };
    private readonly Queue<Action> markdownEdits = new();
    private DispatcherOperation markdownCommit;
    private bool typingPending;
    private int changeDepth;

    internal NoteEditor(string markdown) : base(Render(markdown))
    {
        // WPF stamps a different input language onto the first typed run,
        // which clears pending character formatting. Match the input layout
        // when loading a note so its first keystroke keeps those marks.
        Document.Language = XmlLanguage.GetLanguage(InputLanguageManager.Current.CurrentInputLanguage.IetfLanguageTag);
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
            typingPending = false;
            if (changeDepth != 0) return;
            DocumentEdited?.Invoke();
            QueueMarkdownInput(e);
        };
        CommandManager.AddPreviewExecutedHandler(this, OnEditingCommand);
        AddHandler(TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler((_, _) => typingPending = !IsReadOnly), handledEventsToo: true);
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(TaskChanged));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(TaskChanged));
        DataObject.AddCopyingHandler(this, (_, e) => e.DataObject.SetData(FormattedNoteClipboard, "1", false));
        DataObject.AddPastingHandler(this, Paste);
    }

    internal event Action DocumentEdited;
    internal string PlainText => new TextRange(Document.ContentStart, Document.ContentEnd).Text.TrimEnd('\r', '\n');
    internal string Markdown => MarkdownWriter.Write(Document);

    private void TaskChanged(object sender, RoutedEventArgs e)
    {
        if (changeDepth == 0) DocumentEdited?.Invoke();
    }

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        if (Selection.IsEmpty)
        {
            if (CaretPosition.Paragraph is { } paragraph) yield return paragraph;
            yield break;
        }
        var found = new HashSet<Paragraph>();
        var end = Selection.End;
        if (end.Paragraph is { } last && end.CompareTo(NoteLists.TextStart(last)) <= 0)
            end = last.ElementStart;
        for (var point = Selection.Start; point != null && point.CompareTo(end) < 0;
             point = point.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (point.Paragraph is { } paragraph && found.Add(paragraph)) yield return paragraph;
        }
    }

    private void QueueMarkdownInput(TextChangedEventArgs e)
    {
        if (IsReadOnly || !Selection.IsEmpty || e.UndoAction is UndoAction.Undo or UndoAction.Redo ||
            CaretPosition.Paragraph is not { } paragraph || FormatOf(paragraph) is CodeBlockInfo or BlockStyle.Divider)
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

        var textStart = NoteLists.TextStart(paragraph);
        if (beforeTrigger.CompareTo(textStart) < 0) return;
        var prefix = NoteLists.InlineText(textStart, beforeTrigger);
        if (input == " " && QueueBlockShortcut(paragraph, prefix, caret)) return;
        var candidate = input == Backtick ? prefix + input : prefix;
        if (candidate.Length == 0 || candidate[^1] is not ('*' or '_' or '~' or '+' or (char)0x60 or ')' or '>') ||
            ReadInlineShortcut(candidate, input == Backtick) is not { } shortcut)
            return;

        var start = MoveBackByText(beforeTrigger, prefix.Length - shortcut.Start);
        if (start == null) return;
        ApplyAfterInput(start, caret, (range, atTrigger) =>
        {
            var selectionStart = Selection.Start.GetPositionAtOffset(0, LogicalDirection.Forward);
            var selectionEnd = Selection.End.GetPositionAtOffset(0, LogicalDirection.Forward);
            var backward = IsBackwardSelection();
            range.Text = "";
            if (input == " ") shortcut.Content.Inlines.Add(new Run(" "));
            InsertInlines(shortcut.Content, range.Start);
            if (input == Backtick)
            {
                Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, Document.FontFamily);
                Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
            if (!atTrigger)
            {
                if (backward) Selection.Select(selectionEnd, selectionStart);
                else Selection.Select(selectionStart, selectionEnd);
            }
        });
    }

    private bool QueueBlockShortcut(Paragraph paragraph, string prefix, TextPointer end)
    {
        Action format;
        if (prefix.Length is >= 1 and <= 6 && prefix.All(character => character == '#'))
            format = () => SetParagraphHeading([paragraph], prefix.Length);
        else if (prefix is "-" or "*" or "+")
            format = () => ApplyList([paragraph], NoteLists.Kind.Bullets);
        else if (prefix.Length is >= 2 and <= 10 && prefix[^1] is '.' or ')' &&
                 prefix[..^1].All(char.IsAsciiDigit) && int.TryParse(prefix[..^1], out var start) && start > 0)
            format = () => ApplyList([paragraph], NoteLists.Kind.Numbers, start);
        else if (prefix is "[]" or "[ ]" or "[x]" or "- [ ]" or "- [x]")
            format = () =>
            {
                ApplyList([paragraph], NoteLists.Kind.Tasks);
                ((CheckBox)NoteLists.TaskOf(paragraph).Child).IsChecked = prefix.Contains('x');
            };
        else if (prefix == ">")
            format = () => WrapQuote(paragraph);
        else if (prefix == Backtick + Backtick + Backtick)
            format = () => SetCodeBlock(paragraph, "");
        else return false;

        ApplyAfterInput(NoteLists.TextStart(paragraph), end, (marker, atTrigger) =>
        {
            marker.Text = "";
            var selection = atTrigger ? null : SaveSelection();
            format();
            if (selection == null) CaretPosition = NoteLists.TextStart(paragraph);
            else RestoreSelection(selection);
        });
        return true;
    }

    private void ApplyAfterInput(TextPointer start, TextPointer end, Action<TextRange, bool> apply)
    {
        var document = Document;
        start = start.GetPositionAtOffset(0, LogicalDirection.Forward);
        end = end.GetPositionAtOffset(0, LogicalDirection.Backward);
        var original = new TextRange(start, end).Text;

        // WPF batches keystrokes. Wait for the native edit to finish, and keep
        // this range anchored before any characters typed after the shortcut.
        markdownEdits.Enqueue(() =>
        {
            if (IsReadOnly || !ReferenceEquals(Document, document) || start.CompareTo(end) >= 0) return;
            var range = new TextRange(start, end);
            if (range.Text != original) return;
            var caretAtTrigger = Selection.IsEmpty && CaretPosition.CompareTo(end) == 0;
            Change(() => apply(range, caretAtTrigger));
        });
        markdownCommit ??= Dispatcher.InvokeAsync(CommitMarkdown, DispatcherPriority.Input);
    }

    private void CommitTyping()
    {
        if (typingPending)
        {
            // Let the native text input queued before this command commit.
            typingPending = false;
            Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        }
        CommitMarkdown();
    }

    private void CommitMarkdown()
    {
        markdownCommit?.Abort();
        markdownCommit = null;
        while (markdownEdits.TryDequeue(out var edit)) edit();
    }

    private void WrapQuote(Paragraph paragraph)
    {
        var blocks = NoteLists.BlocksOf(paragraph);
        var quote = Quote();
        blocks.InsertBefore(paragraph, quote);
        blocks.Remove(paragraph);
        quote.Blocks.Add(paragraph);
        CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
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
        if (fragment.Blocks.Count == 1 && fragment.Blocks.FirstBlock is Paragraph source && FormatOf(source) == null)
        {
            InsertInlines(source, caret);
            return;
        }

        var before = NoteLists.InlineText(paragraph.ContentStart, caret).Length > 0;
        var after = NoteLists.InlineText(caret, paragraph.ContentEnd).Length > 0;
        if (!after && paragraph.Parent is ListItem { Parent: List list } item && item.Blocks.FirstBlock == paragraph &&
            fragment.Blocks.Count == 1 && fragment.Blocks.FirstBlock is List pasted && NoteLists.KindOf(pasted) == NoteLists.KindOf(list))
        {
            var lastItem = item;
            foreach (var row in pasted.ListItems.ToArray())
            {
                pasted.ListItems.Remove(row);
                list.ListItems.InsertAfter(lastItem, row);
                lastItem = row;
            }
            if (!before && item.Blocks.Count == 1) list.ListItems.Remove(item);
            CaretPosition = lastItem.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            return;
        }
        var replace = !before && !after && FormatOf(paragraph) == null && NoteLists.TaskOf(paragraph) == null;
        var anchor = before && after ? caret.InsertParagraphBreak().Paragraph
            : before ? paragraph.NextBlock : paragraph;
        var blocks = NoteLists.BlocksOf(anchor ?? paragraph);
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

    private void Change(Action action, bool preserveSelection = false)
    {
        var selection = preserveSelection ? SaveSelection() : null;
        BeginChange();
        changeDepth++;
        try
        {
            action();
            if (changeDepth == 1)
            {
                selection ??= SaveSelection();
                NoteLists.Normalize(Document);
            }
            if (selection != null) RestoreSelection(selection);
        }
        finally
        {
            EndChange();
            changeDepth--;
        }
        if (changeDepth == 0) DocumentEdited?.Invoke();
    }
}
