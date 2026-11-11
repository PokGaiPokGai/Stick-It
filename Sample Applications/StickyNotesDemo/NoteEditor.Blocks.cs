using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using static StickyNotesDemo.MarkdownView;
using ListKind = StickyNotesDemo.NoteLists.Kind;

namespace StickyNotesDemo;

internal sealed partial class NoteEditor
{
    private bool runningNativeCommand;

    internal void ToggleHeading()
    {
        CommitTyping();
        var paragraphs = FormattableParagraphs();
        if (paragraphs.Length == 0) return;
        var level = paragraphs.All(p => HeadingLevel(p) > 0) ? 0 : 2;
        Change(() => SetParagraphHeading(paragraphs, level), preserveSelection: true);
    }

    internal void ToggleList(bool ordered) => ToggleListKind(ordered ? ListKind.Numbers : ListKind.Bullets);
    internal void ToggleTask() => ToggleListKind(ListKind.Tasks);

    private void ToggleListKind(ListKind kind)
    {
        CommitTyping();
        var paragraphs = FormattableParagraphs();
        if (paragraphs.Length == 0) return;
        var remove = paragraphs.All(p => p.Parent is ListItem { Parent: List list } && NoteLists.KindOf(list) == kind);
        Change(() => ApplyList(paragraphs, remove ? null : kind), preserveSelection: true);
    }

    private Paragraph[] FormattableParagraphs() => SelectedParagraphs()
        .Where(p => FormatOf(p) is not (CodeBlockInfo or BlockStyle.Divider)).ToArray();

    private static void SetParagraphHeading(Paragraph[] paragraphs, int level)
    {
        ApplyList(paragraphs, null);
        foreach (var paragraph in paragraphs) SetHeading(paragraph, level);
    }

    private static void ApplyList(Paragraph[] paragraphs, ListKind? kind, int? startIndex = null)
    {
        if (kind != null)
            foreach (var paragraph in paragraphs)
                if (HeadingLevel(paragraph) > 0) SetHeading(paragraph, 0);

        // Move nested items first so selecting a parent and its children
        // still changes each selected row once.
        foreach (var group in ListGroups(paragraphs).Reverse())
            NoteLists.ChangeItems(group.Key, group.ToHashSet(), kind, startIndex);

        if (kind is { } applied)
            foreach (var paragraph in paragraphs)
                if (paragraph.Parent is not ListItem) NoteLists.Wrap(paragraph, applied, startIndex);
    }

    private static IEnumerable<IGrouping<List, ListItem>> ListGroups(IEnumerable<Paragraph> paragraphs)
        => SelectedItems(paragraphs).GroupBy(item => (List)item.Parent);

    private static IEnumerable<ListItem> SelectedItems(IEnumerable<Paragraph> paragraphs)
        => paragraphs.Select(p => p.Parent).OfType<ListItem>().Distinct();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (IsReadOnly || e.Handled) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = e.KeyboardDevice.Modifiers;
        if (key == Key.Space && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            typingPending = true;

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (key)
            {
                case Key.D7: ToggleListKind(ListKind.Numbers); break;
                case Key.D8: ToggleListKind(ListKind.Bullets); break;
                case Key.D9: ToggleTask(); break;
                case Key.X: ToggleMark(InlineStyle.Strike); break;
                case Key.Z: CommitTyping(); Redo(); break;
                default: return;
            }
        }
        else if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.B: ToggleMark(InlineStyle.Bold); break;
                case Key.I: ToggleMark(InlineStyle.Italic); break;
                case Key.U: ToggleMark(InlineStyle.Underline); break;
                case Key.Z: CommitTyping(); Undo(); break;
                case Key.Y: CommitTyping(); Redo(); break;
                case Key.OemOpenBrackets: if (!ChangeIndentation(increase: false)) return; break;
                case Key.OemCloseBrackets: if (!ChangeIndentation(increase: true)) return; break;
                default: return;
            }
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is >= Key.D0 and <= Key.D6)
        {
            CommitTyping();
            var paragraphs = FormattableParagraphs();
            if (paragraphs.Length != 0)
                Change(() => SetParagraphHeading(paragraphs, key - Key.D0), preserveSelection: true);
        }
        else if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            if (!ChangeIndentation(modifiers == ModifierKeys.None)) return;
        }
        else if (key == Key.Enter && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            CommitTyping();
            if (!CanSplitSelection()) return;
            Change(() => InsertBreak(modifiers == ModifierKeys.Shift));
        }
        else return;

        e.Handled = true;
    }

    private void OnEditingCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (IsReadOnly || runningNativeCommand || e.Command is not RoutedCommand command) return;

        if (command == EditingCommands.ToggleBullets || command == EditingCommands.ToggleNumbering)
            ToggleList(command == EditingCommands.ToggleNumbering);
        else if (command == EditingCommands.ToggleUnderline)
            ToggleMark(InlineStyle.Underline);
        else if (command == EditingCommands.TabForward || command == EditingCommands.TabBackward ||
                 command == EditingCommands.IncreaseIndentation || command == EditingCommands.DecreaseIndentation)
        {
            if (!ChangeIndentation(command == EditingCommands.TabForward || command == EditingCommands.IncreaseIndentation))
                return;
        }
        else if (command == EditingCommands.EnterParagraphBreak || command == EditingCommands.EnterLineBreak)
        {
            CommitTyping();
            if (!CanSplitSelection()) return;
            Change(() => InsertBreak(command == EditingCommands.EnterLineBreak));
        }
        else if (command == EditingCommands.Backspace || command == EditingCommands.Delete ||
                 command == EditingCommands.DeletePreviousWord || command == EditingCommands.DeleteNextWord ||
                 command == ApplicationCommands.Cut)
        {
            CommitTyping();
            Change(() => DeleteText(command, e.Parameter));
        }
        else if (command == ApplicationCommands.Paste)
        {
            CommitTyping();
            Change(() => RunNative(command, e.Parameter));
        }
        else return;

        e.Handled = true;
    }

    private bool CanSplitSelection()
    {
        if (Selection.Start.Paragraph == null || Selection.End.Paragraph == null) return false;
        // Leave table-row creation and hyperlinks to the native editor.
        for (var element = Selection.Start.Parent as TextElement; element != null; element = element.Parent as TextElement)
            if (element is Hyperlink or TableCell) return false;
        return true;
    }

    private void InsertBreak(bool soft)
    {
        if (!Selection.IsEmpty) Selection.Text = "";
        var paragraph = CaretPosition.Paragraph;
        var start = NoteLists.TextStart(paragraph);
        if (CaretPosition.CompareTo(start) < 0) CaretPosition = start;

        if (soft || FormatOf(paragraph) is CodeBlockInfo)
        {
            CaretPosition = CaretPosition.InsertLineBreak();
            return;
        }

        var level = HeadingLevel(paragraph);
        var empty = NoteLists.IsEmpty(paragraph);
        if (paragraph.Parent is ListItem { Parent: List list } item && item.Blocks.FirstBlock == paragraph && empty)
        {
            OutdentItem(paragraph, item, list);
            return;
        }
        if (level > 0 && empty)
        {
            ResetEmptyHeading(paragraph);
            return;
        }

        var atStart = CaretPosition.CompareTo(start) <= 0;
        var atEnd = NoteLists.InlineText(CaretPosition, paragraph.ContentEnd).Length == 0;
        var task = NoteLists.TaskOf(paragraph);
        var next = CaretPosition.InsertParagraphBreak().Paragraph;
        if (task != null) NoteLists.ContinueTask(paragraph, next, task, atStart);
        if (level > 0)
        {
            SetHeading(paragraph, atStart ? 0 : level);
            SetHeading(next, atEnd ? 0 : level);
        }
        CaretPosition = NoteLists.TextStart(next);
        if (level > 0 && atEnd) ResetTypingStyle(next);
    }

    private void DeleteText(RoutedCommand command, object parameter)
    {
        var affected = SelectedParagraphs().ToArray();
        if (Selection.IsEmpty && CaretPosition.Paragraph is { } paragraph)
        {
            var backward = command == EditingCommands.Backspace || command == EditingCommands.DeletePreviousWord;
            if (backward && CaretPosition.CompareTo(NoteLists.TextStart(paragraph)) <= 0)
            {
                if (HeadingLevel(paragraph) > 0)
                {
                    if (NoteLists.IsEmpty(paragraph)) ResetEmptyHeading(paragraph);
                    else SetHeading(paragraph, 0);
                    return;
                }
                if (paragraph.Parent is ListItem { Parent: List list } item && item.Blocks.FirstBlock == paragraph)
                {
                    OutdentItem(paragraph, item, list);
                    return;
                }
            }
            if (!backward && NoteLists.TaskOf(paragraph) != null &&
                CaretPosition.CompareTo(NoteLists.TextStart(paragraph)) < 0)
                CaretPosition = NoteLists.TextStart(paragraph);
        }

        RunNative(command, parameter);
        foreach (var line in affected)
        {
            if (!IsAttached(line)) continue;
            if (HeadingLevel(line) > 0 && NoteLists.IsEmpty(line)) ResetEmptyHeading(line);
            if (line.Parent is ListItem { Parent: List list } item && item.Blocks.FirstBlock == line &&
                NoteLists.KindOf(list) == ListKind.Tasks)
                NoteLists.SetTask(line, true);
        }
    }

    private void ResetEmptyHeading(Paragraph paragraph)
    {
        SetHeading(paragraph, 0);
        if (CaretPosition.Paragraph == paragraph) ResetTypingStyle(paragraph);
    }

    private void ResetTypingStyle(Paragraph paragraph)
    {
        Selection.ApplyPropertyValue(TextElement.FontSizeProperty, paragraph.FontSize);
        Selection.ApplyPropertyValue(TextElement.FontWeightProperty, paragraph.FontWeight);
        Selection.ApplyPropertyValue(TextElement.FontStyleProperty, paragraph.FontStyle);
        Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, paragraph.FontFamily);
        Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        Selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
    }

    private bool ChangeIndentation(bool increase)
    {
        CommitTyping();
        var items = NoteLists.OutermostItems(SelectedItems(SelectedParagraphs()).ToArray());
        if (items.Length == 0) return false;
        if (increase)
        {
            Change(() => NoteLists.Indent(items), preserveSelection: true);
            return true;
        }
        Change(() =>
        {
            foreach (var group in items.GroupBy(item => (List)item.Parent).Reverse())
            {
                var list = group.Key;
                if (list.Parent is not ListItem)
                    NoteLists.ChangeItems(list, group.ToHashSet(), null);
                else
                {
                    var start = group.First().ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                    var end = group.Last().ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
                    Selection.Select(start, end);
                    RunNative(EditingCommands.DecreaseIndentation);
                }
            }
        }, preserveSelection: true);
        return true;
    }

    private void OutdentItem(Paragraph paragraph, ListItem item, List list)
    {
        if (list.Parent is ListItem)
            RunNative(EditingCommands.DecreaseIndentation);
        else
            NoteLists.ChangeItems(list, [item], null);
        CaretPosition = NoteLists.TextStart(paragraph);
    }

    private void RunNative(RoutedCommand command, object parameter = null)
    {
        runningNativeCommand = true;
        try { command.Execute(parameter, this); }
        finally { runningNativeCommand = false; }
    }

    private sealed record Position(Paragraph Paragraph, int Offset, TextPointer Pointer)
    {
        internal static Position Save(TextPointer point)
        {
            var paragraph = point.Paragraph;
            var offset = paragraph == null ? 0 : Math.Max(0, NoteLists.TextStart(paragraph).GetOffsetToPosition(point));
            return new Position(paragraph, offset, point);
        }

        internal TextPointer Restore(NoteEditor editor)
        {
            if (Paragraph == null || !editor.IsAttached(Paragraph)) return Pointer;
            var start = NoteLists.TextStart(Paragraph);
            var length = start.GetOffsetToPosition(Paragraph.ContentEnd);
            return start.GetPositionAtOffset(Math.Min(Offset, length), Pointer.LogicalDirection);
        }
    }

    private sealed record SavedSelection(Position Start, Position End, bool Backward);
    private SavedSelection SaveSelection() => new(Position.Save(Selection.Start), Position.Save(Selection.End), IsBackwardSelection());
    private bool IsBackwardSelection() => !Selection.IsEmpty && CaretPosition.CompareTo(Selection.Start) <= 0;
    private void RestoreSelection(SavedSelection saved)
    {
        var start = saved.Start.Restore(this);
        var end = saved.End.Restore(this);
        if (start.CompareTo(Selection.Start) == 0 && end.CompareTo(Selection.End) == 0 && saved.Backward == IsBackwardSelection())
            return;
        if (saved.Backward) Selection.Select(end, start);
        else Selection.Select(start, end);
    }

    private bool IsAttached(Paragraph paragraph)
    {
        DependencyObject root = paragraph;
        while (root is TextElement element) root = element.Parent;
        return ReferenceEquals(root, Document);
    }
}
