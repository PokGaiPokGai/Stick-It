using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private static void TestFormattingToolbar(string output)
    {
        var note = new EditorSession("## 标题\n\n正文\n\n- 项目\n\n1. 编号\n\n- [x] 完成\n\n```cs\nvar x = 1;\n```");
        var bar = FormattingBar(note.Window);
        var heading = FindFormatButton(bar, "标题");
        var bold = FindFormatButton(bar, "粗体");
        var bullets = FindFormatButton(bar, "项目符号");
        var numbers = FindFormatButton(bar, "编号");
        var tasks = FindFormatButton(bar, "待办");
        var code = FindFormatButton(bar, "行内代码");
        note.End(note.Body());
        DrainFormatting();
        Assert(heading.IsChecked == true && bold.IsChecked == false, "heading shows active without treating its font weight as bold");
        note.Select(note.Body(), note.Body(1));
        DrainFormatting();
        Assert(heading.IsChecked == null && FormatToggle(heading).ToggleState == ToggleState.Indeterminate,
               "heading and body selection has an accessible mixed state");

        note.End(note.Row(2));
        DrainFormatting();
        Assert(bullets.IsChecked == true && numbers.IsChecked == false && tasks.IsChecked == false, "caret in a bullet shows the bullet button only");
        note.End(note.Row(3));
        DrainFormatting();
        Assert(bullets.IsChecked == false && numbers.IsChecked == true && tasks.IsChecked == false, "caret in numbering updates the active list button");
        note.End(note.Row(4));
        DrainFormatting();
        Assert(bullets.IsChecked == false && numbers.IsChecked == false && tasks.IsChecked == true, "checked task has the same task format as an unchecked row");
        note.Select(note.Row(2), note.Row(4));
        DrainFormatting();
        Assert(bullets.IsChecked == null && numbers.IsChecked == null && tasks.IsChecked == null, "each list type in a mixed selection is indicated");
        note.End(note.Body(5));
        DrainFormatting();
        Assert(code.IsChecked == false && bar.Children.OfType<ToggleButton>().All(button => button.IsChecked == false),
               "a fenced code block does not activate inline code or list buttons");

        note = new EditorSession("");
        bar = FormattingBar(note.Window);
        bold = FindFormatButton(bar, "粗体");
        var italic = FindFormatButton(bar, "斜体");
        var underline = FindFormatButton(bar, "下划线");
        var strike = FindFormatButton(bar, "删除线");
        code = FindFormatButton(bar, "行内代码");
        note.Key(Key.B, ModifierKeys.Control);
        note.Key(Key.I, ModifierKeys.Control);
        note.Key(Key.U, ModifierKeys.Control);
        note.Key(Key.X, FormatShortcut);
        FormatToggle(code).Toggle();
        DrainFormatting();
        Assert(new[] { bold, italic, underline, strike, code }.All(button => button.IsChecked == true) && note.Markdown == "",
               "pending typing marks activate before text is inserted");
        var languageBefore = note.Editor.CaretPosition.Parent.GetValue(FrameworkElement.LanguageProperty);
        note.Type("code");
        Assert(note.Markdown == "<s><u><em><strong>`code`</strong></em></u></s>" &&
               new[] { bold, italic, underline, strike, code }.All(button => button.IsChecked == true),
               $"the active typing marks are applied to the new text ({note.Markdown}, {bold.IsChecked}/{italic.IsChecked}/{underline.IsChecked}/{strike.IsChecked}/{code.IsChecked}, document language {languageBefore}, input language {InputLanguageManager.Current.CurrentInputLanguage.Name})");
        note.Key(Key.B, ModifierKeys.Control);
        Assert(bold.IsChecked == false && italic.IsChecked == true && code.IsChecked == true && note.Markdown.Contains("<strong>", StringComparison.Ordinal),
               "switching off bold at the caret leaves the earlier text and other typing marks intact");
        note.Key(Key.Right);
        Assert(bold.IsChecked == true, "navigation at the end of the text clears the pending override and refreshes the toolbar");

        note = new EditorSession("<u>甲</u><s><u>乙</u></s>");
        bar = FormattingBar(note.Window);
        underline = FindFormatButton(bar, "下划线");
        strike = FindFormatButton(bar, "删除线");
        note.Select(note.Body(), note.Body());
        DrainFormatting();
        Assert(underline.IsChecked == true && strike.IsChecked == null, "overlapping marks each report their own coverage");
        FormatToggle(underline).Toggle();
        DrainFormatting();
        Assert(underline.IsChecked == false && strike.IsChecked == null && note.Markdown == "甲<s>乙</s>",
               "turning off a fully active mark preserves the partly applied mark");
        FormatToggle(strike).Toggle();
        DrainFormatting();
        Assert(strike.IsChecked == true, "activating a partly applied mark applies it to the full selection");
        note.Key(Key.Z, ModifierKeys.Control);
        Assert(strike.IsChecked == null, "undo updates the toolbar without moving the selection");
        note.Key(Key.Z, FormatShortcut);
        Assert(strike.IsChecked == true, "redo restores the active style");

        note = new EditorSession("");
        bar = FormattingBar(note.Window);
        heading = FindFormatButton(bar, "标题");
        note.Type("##", drain: false);
        note.Key(Key.Space);
        Assert(heading.IsChecked == true, "Markdown autoformat updates the toolbar");
        note.Type("标题");
        note.Key(Key.Enter);
        Assert(heading.IsChecked == false, "leaving a heading updates the toolbar for the new body paragraph");

        note = new EditorSession("- <em><strong>粗体斜体</strong> 与普通斜体</em>\n- <em>第二个项目</em>");
        bar = FormattingBar(note.Window);
        note.Select(note.Row(0), note.Row(0, 1));
        DrainFormatting();
        Render((FrameworkElement)note.Window.Content, 320, 320, Path.Combine(output, "formatting-toolbar-preview.png"));
        var inactive = FindFormatButton(bar, "标题");
        var mixed = FindFormatButton(bar, "粗体");
        var active = FindFormatButton(bar, "斜体");
        var activeList = FindFormatButton(bar, "项目符号");
        Assert(mixed.IsChecked == null && active.IsChecked == true && activeList.IsChecked == true,
               "toolbar shows partial inline, full inline and block styles together");
        Assert(Highlight(active).Background is SolidColorBrush { Color.A: 255 } &&
               Highlight(mixed).BorderBrush is SolidColorBrush { Color.A: 255 } &&
               Highlight(mixed).Background is SolidColorBrush { Color.A: 0 } &&
               Highlight(inactive).BorderBrush is SolidColorBrush { Color.A: 0 },
               "offscreen WPF template draws different active, mixed and inactive states");
    }

    private static UniformGrid FormattingBar(Window2 window)
        => (UniformGrid)typeof(Window2).GetField("footer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);

    private static ToggleButton FindFormatButton(UniformGrid bar, string name)
        => bar.Children.OfType<ToggleButton>().Single(button => ((string)button.ToolTip).StartsWith(name, StringComparison.Ordinal));

    private static IToggleProvider FormatToggle(ToggleButton button)
        => (IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle);

    private static Border Highlight(ToggleButton button) => (Border)button.Template.FindName("Highlight", button);

    private static void DrainFormatting() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
