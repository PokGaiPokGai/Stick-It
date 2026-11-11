using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); Console.WriteLine("PASS "+message); }
    [STAThread] static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="contender") { using var other=new SingleInstance(args[1]); return other.IsPrimary ? 9 : 0; }
        if(args.Length>0 && args[0]=="abandon") { var owner=new SingleInstance(args[1]); Console.WriteLine(owner.IsPrimary?"ready":"failed"); Console.Out.Flush(); Thread.Sleep(30000); return 0; }
        var output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        var temp=Path.Combine(Path.GetTempPath(),"StickIt-check-"+Guid.NewGuid()); Directory.CreateDirectory(temp);
        try {
            TestSingleInstance(); TestPlacement();
            var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };

            TestStore(temp); TestMarkdown(); TestWindows(temp,output); TestEditorBlocks(temp, output);
            Console.WriteLine("All checks passed. No window, tray icon, hotkey, or desktop input was activated."); return 0;
        } catch(Exception error) { Console.Error.WriteLine(error); return 1; } finally { Directory.Delete(temp,true); }
    }
    static Process Child(string mode,string name)
    {
        var start=new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true };
        start.ArgumentList.Add(mode); start.ArgumentList.Add(name);
        return Process.Start(start);
    }
    static void TestSingleInstance()
    {
        string name=@"Local\StickIt.Test."+Guid.NewGuid();
        using(var first=new SingleInstance(name)) {
            Assert(first.IsPrimary,"first process owns singleton");
            using var received=new ManualResetEventSlim(); first.Listen(()=>received.Set());
            var others=Enumerable.Range(0,8).Select(_=>Child("contender",name)).ToArray();
            foreach(var child in others) { Assert(child.WaitForExit(10000)&&child.ExitCode==0,"duplicate process exits without ownership"); child.Dispose(); }
            Assert(received.Wait(5000),"duplicate launch signals existing process");
        }
        using(var replacement=new SingleInstance(name)) Assert(replacement.IsPrimary,"ownership released on normal exit");
        name+= ".Crash";
        using(var crashed=Child("abandon",name)) { Assert(crashed.StandardOutput.ReadLine()=="ready","crash test owns mutex"); crashed.Kill(); crashed.WaitForExit(); }
        using(var recovered=new SingleInstance(name)) Assert(recovered.IsPrimary,"startup recovers after owner process dies");
    }
    static void TestPlacement()
    {
        var work=new DesktopIntegration.Rect { Left=-1920,Top=0,Right=0,Bottom=1040 };
        var centered=DesktopIntegration.CenteredBounds(new DesktopIntegration.Point {X=-960,Y=520},work,480,450);
        Assert(centered.Left==-1200 && centered.Top==295,"centered on cursor with scaled pixel dimensions");
        var edge=DesktopIntegration.CenteredBounds(new DesktopIntegration.Point {X=-1919,Y=1039},work,480,450);
        Assert(edge.Left==-1920&&edge.Bottom==1040,"clamped to negative-coordinate monitor work area");
        var shortWorkArea = new DesktopIntegration.Rect { Right = 640, Bottom = 240 };
        var square = DesktopIntegration.CenteredBounds(new DesktopIntegration.Point { X = 320, Y = 120 }, shortWorkArea, 320, 320);
        Assert(square.Right - square.Left == 240 && square.Bottom - square.Top == 240, "square note stays square when reduced to fit the work area");
        Assert(DesktopIntegration.HotkeyModifiers==0x4005&&DesktopIntegration.HotkeyKey==0x57,"Alt Shift W global shortcut with no repeat");
    }
    static void TestStore(string temp)
    {
        var dir=Path.Combine(temp,"store"); var store=new NoteStore(dir);
        var data=new NoteData { Markdown="# 标题\n- [x] **完成**", IsOpen=false, Color="#E2F1FF" };
        store.Save(new List<NoteData>{data}); var loaded=store.Load();
        Assert(loaded.Count==1&&loaded[0].Id==data.Id&&loaded[0].Markdown==data.Markdown&&!loaded[0].IsOpen,"markdown, identity, color and closed state round-trip");
        data.Markdown="changed"; store.Save(new List<NoteData>{data});
        Assert(File.Exists(Path.Combine(dir,"notes.json.bak")),"prior saved file retained as backup");
        File.WriteAllText(Path.Combine(dir,"notes.json"),"null");
        try { store.Load(); throw new Exception("invalid input accepted"); } catch(InvalidDataException) { Console.WriteLine("PASS invalid store rejected"); }
        var legacy=Path.Combine(temp,"legacy"); Directory.CreateDirectory(legacy);
        var doc=new FlowDocument(new Paragraph(new Run("旧便签内容")));
        using(var stream=File.Create(Path.Combine(legacy,"0"))) new TextRange(doc.ContentStart,doc.ContentEnd).Save(stream,DataFormats.Xaml);
        File.WriteAllLines(Path.Combine(legacy,"NotesFile"),new[]{"1|0|0|60|120|True|-1|-1|00:00:00|2026/11/11|null","1|20|30|200|200|True|2|-1|00:00:00|0001/1/1|null"});
        var imported=new NoteStore(legacy).Load();
        Assert(imported.Single().Markdown=="旧便签内容" && imported[0].Color=="#E2F1FF" && File.Exists(Path.Combine(legacy,"0")),"legacy sample text imported without deleting original files");
    }
    static void TestMarkdown()
    {
        var doc=MarkdownView.Render("# Heading\n\n**bold** *italic* ~~strike~~ `code` [site](https://example.com)\n\n- [x] done\n- [ ] todo\n\n> quote\n\n```cs\nvar x = 1;\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |");
        Assert(doc.Blocks.FirstBlock is Paragraph p && p.FontSize==26,"Markdown heading renders");
        var taskList = doc.Blocks.OfType<System.Windows.Documents.List>().Single();
        var firstTask = (CheckBox)((InlineUIContainer)((Paragraph)taskList.ListItems.FirstListItem.Blocks.FirstBlock).Inlines.FirstInline).Child;
        Assert(firstTask.IsChecked == true, "Markdown task list renders a checked task");
        Assert(doc.Blocks.OfType<Table>().Any(),"Markdown table renders");
        var text=new TextRange(doc.ContentStart,doc.ContentEnd).Text;
        Assert(text.Contains("var x = 1;")&&!text.Contains("**bold**"),"fenced code and emphasis render without syntax delimiters");
        var writer = typeof(MarkdownView).Assembly.GetType("StickyNotesDemo.MarkdownWriter");
        var saved = (string)writer.GetMethod("Write", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, [doc]);
        var reopened = MarkdownView.Render(saved);
        Assert(reopened.Blocks.OfType<Section>().Any() && reopened.Blocks.OfType<Table>().Any() &&
               saved.Contains("https://example.com", StringComparison.Ordinal) &&
               saved.Contains("- [x] done", StringComparison.Ordinal) &&
               saved.Contains("\u0060\u0060\u0060cs\nvar x = 1;", StringComparison.Ordinal),
               "rich document storage preserves quotes, tables, links, tasks and fenced code");
    }
    static object Invoke(object obj,string method,params object[] values) => obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,values);
    static void TestWindows(string temp,string output)
    {
        var noteDirectory = Path.Combine(temp, "ui");
        Directory.CreateDirectory(noteDirectory);
        File.WriteAllText(Path.Combine(noteDirectory, "notes.json"), """[{"Pinned":false}]""");
        var store = new NoteStore(noteDirectory);
        var main = new MainWindow(store);
        Application.Current.MainWindow=main;
        var data = store.Load().Single();
        data.Markdown = "## Blender HairMesh 插件问题\n- 数值恢复默认值\n- 保存当前面板状态\n- 检查 UI 逻辑\n- 调整 crown 卡片";
        data.Color = "#E2F1FF";
        data.Modified = DateTime.Today.AddHours(15).AddMinutes(29);
        var note=(Window2)Invoke(main,"AddNote",data);
        Invoke(main,"AddNote",new NoteData { Markdown="fork 一下 HairMesh 仓库，自行优化",Modified=DateTime.Today.AddHours(3).AddMinutes(34) });
        Invoke(main,"AddNote",new NoteData { Markdown="随手记录一个想法",Modified=DateTime.Today.AddHours(2).AddMinutes(43) });
        Invoke(main,"RefreshList");
        Assert(!note.ShowInTaskbar && main.ShowInTaskbar,"only library window participates in taskbar");
        Assert(note.Topmost && note.Background==Brushes.Transparent && ((Border)note.Content).Background is SolidColorBrush,"previously unpinned note stays on top and uses solid rounded paper");
        var noteWidth = (int)note.Width;
        var noteHeight = (int)note.Height;
        Assert(noteWidth == 320 && noteHeight == 320, "default note opens as a 320 by 320 square");
        var noteSurface = (FrameworkElement)note.Content;
        Render((FrameworkElement)main.Content,478,720,Path.Combine(output,"library-preview.png"));
        Render(noteSurface,noteWidth,noteHeight,Path.Combine(output,"note-preview.png"));
        var tasks = new Window2(new NoteData { Markdown = "## 今日待办\n\n- [ ] 整理设计参考\n- [x] 保存便签改动" });
        Render((FrameworkElement)tasks.Content, (int)tasks.Width, (int)tasks.Height, Path.Combine(output, "tasks-preview.png"));
        var search=(TextBox)main.FindName("SearchBox"); search.Text="fork";
        var list=(ItemsControl)main.FindName("NoteList");
        Assert(list.Items.Count==1,"search filters library cards");
        var editor=Find<RichTextBox>((DependencyObject)note.Content);
        var document = editor.Document;
        var formatBar = (UniformGrid)typeof(Window2).GetField("footer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(note);
        var body = (FrameworkElement)VisualTreeHelper.GetParent(editor);
        Rect BodyBounds() => new Rect(body.TranslatePoint(new Point(), noteSurface), body.RenderSize);
        var readingBounds = BodyBounds();
        Invoke(note,"BeginEditing");
        Render(noteSurface,noteWidth,noteHeight,Path.Combine(output,"note-editing-preview.png"));
        Assert(BodyBounds() == readingBounds, "showing the toolbar keeps the body origin and size");
        Assert(!editor.IsReadOnly && ReferenceEquals(document, editor.Document) && formatBar.Visibility == Visibility.Visible, "editing keeps the formatted document and shows bottom tools");
        Invoke(note,"FinishEditing");
        noteSurface.UpdateLayout();
        Assert(BodyBounds() == readingBounds, "hiding the toolbar keeps the body origin and size");
        Assert(editor.IsReadOnly && ReferenceEquals(document, editor.Document) && formatBar.Visibility == Visibility.Collapsed, "finishing editing keeps the same document and hides bottom tools");
        CheckRichEditing(note, editor, formatBar);
        editor.Document.Blocks.Clear();
        editor.Document.Blocks.Add(new Paragraph());
        var hint=(TextBlock)typeof(Window2).GetField("placeholder",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(note);
        Assert(hint.Visibility==Visibility.Visible,"empty inactive note shows placeholder");
        Invoke(note,"BeginEditing");
        Assert(hint.Visibility==Visibility.Collapsed,"placeholder hidden before caret enters empty editor");
        Invoke(note,"FinishEditing");
        Assert(hint.Visibility==Visibility.Visible,"empty note restores placeholder after editing");
        var close=new CancelEventArgs(); Invoke(note,"OnClosing",close);
        Assert(close.Cancel&&!data.IsOpen,"closing note hides it and keeps data");
        var libraryClose=new CancelEventArgs(); Invoke(main,"OnClosing",libraryClose);
        Assert(libraryClose.Cancel&&!main.ShowInTaskbar,"closing library removes taskbar entry without exiting");
    }
    static void CheckRichEditing(Window2 note, RichTextBox editor, UniformGrid formatBar)
    {
        Invoke(note, "BeginEditing");
        var paragraph = (Paragraph)editor.Document.Blocks.FirstBlock;
        editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        void ClickTool(string name)
        {
            var button = formatBar.Children.OfType<Button>().Single(item => ((string)item.ToolTip).StartsWith(name, StringComparison.Ordinal));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        ClickTool("粗体");
        ClickTool("斜体");
        ClickTool("下划线");
        ClickTool("删除线");
        var restored = (Paragraph)MarkdownView.Render(note.Data.Markdown).Blocks.FirstBlock;
        var formatted = (Run)restored.Inlines.FirstInline;
        Assert(formatted.FontWeight == FontWeights.Bold && formatted.FontStyle == FontStyles.Italic &&
               formatted.TextDecorations.Count == 2 && !editor.Selection.IsEmpty, "bottom formatting applies to the selection and survives reopening");

        void EnterText(string text)
        {
            var line = new Paragraph(new Run(text));
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(line);
            editor.CaretPosition = line.ContentEnd;
        }

        var keyboard = new OfflineKeyboard();
        var inputSource = new OfflineInputSource { RootVisual = editor };
        void DrainInput() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        void Type(string text)
        {
            var composition = new TextComposition(InputManager.Current, editor, text);
            editor.RaiseEvent(new TextCompositionEventArgs(keyboard, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
        }
        void PressSpace(bool drain = true)
        {
            var key = new KeyEventArgs(keyboard, inputSource, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            editor.RaiseEvent(key);
            if (!key.Handled)
            {
                key.RoutedEvent = Keyboard.KeyDownEvent;
                editor.RaiseEvent(key);
            }
            if (drain) DrainInput();
        }

        EnterText("列表内容");
        ClickTool("项目符号");
        Assert(note.Data.Markdown.StartsWith("- 列表内容", StringComparison.Ordinal), "bottom tools create a bullet list");
        ClickTool("编号");
        Assert(note.Data.Markdown.StartsWith("1. 列表内容", StringComparison.Ordinal), "bottom tools convert a bullet list to numbering");
        ClickTool("待办");
        var taskParagraph = (Paragraph)((System.Windows.Documents.List)editor.Document.Blocks.FirstBlock).ListItems.FirstListItem.Blocks.FirstBlock;
        var check = (CheckBox)taskParagraph.Inlines.OfType<InlineUIContainer>().Single().Child;
        check.IsChecked = true;
        Assert(note.Data.Markdown.StartsWith("- [x] 列表内容", StringComparison.Ordinal), "task checkbox saves its checked state");

        EnterText("code");
        editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);
        ClickTool("行内代码");
        Assert(note.Data.Markdown.Contains("\u0060code\u0060", StringComparison.Ordinal), "bottom inline code tool persists the selected text");

        EnterText("");
        Type("#");
        Type("#");
        PressSpace(drain: false);
        Type("标题");
        DrainInput();
        Assert(note.PlainText == "标题" && ((Paragraph)editor.Document.Blocks.FirstBlock).FontSize == 22,
               "queued WPF typing and Space turn the prefix into a heading");
        EnterText("已有文字 **粗体**");
        PressSpace(drain: false);
        Type("接着写");
        DrainInput();
        Assert(note.Data.Markdown.Contains("<strong>粗体</strong> 接着写", StringComparison.Ordinal) &&
               !note.PlainText.Contains("**", StringComparison.Ordinal), "the WPF space key route converts inline Markdown");
        editor.Undo();
        Assert(note.PlainText == "已有文字 **粗体** 接着写", "undo restores the Markdown syntax without dropping subsequent input");
        editor.Redo();
        Assert(note.Data.Markdown.Contains("<strong>粗体</strong>", StringComparison.Ordinal), "redo restores rich formatting");
        EnterText("");
        Type("-");
        PressSpace(drain: false);
        Type("列表");
        DrainInput();
        Assert(note.Data.Markdown.StartsWith("- 列表", StringComparison.Ordinal), "queued Space also formats a list prefix");
        EnterText("普通文字");
        PressSpace();
        Assert(note.PlainText == "普通文字 ", "ordinary spaces keep their native behavior");
        EnterText("**文字**");
        Type("\u3000");
        DrainInput();
        Assert(note.Data.Markdown.Contains("<strong>文字</strong>", StringComparison.Ordinal), "committed ideographic space converts Markdown");
        EnterText("前文");
        var clipboard = new DataObject();
        clipboard.SetData(DataFormats.UnicodeText, "## 粘贴标题\n\n- [x] 完成\n- [ ] 下一步");
        clipboard.SetData(DataFormats.Rtf, @"{\rtf1\ansi Markdown source}");
        var paste = new DataObjectPastingEventArgs(clipboard, false, DataFormats.Rtf);
        editor.RaiseEvent(paste);
        Assert(paste.CommandCancelled &&
               note.PlainText.Contains("前文", StringComparison.Ordinal) &&
               note.Data.Markdown.Contains("## 粘贴标题", StringComparison.Ordinal) &&
               note.Data.Markdown.Contains("- [x] 完成", StringComparison.Ordinal), "the WPF paste route formats Markdown even when RTF is preferred");
        EnterText("");
        var textClipboard = new DataObject(DataFormats.UnicodeText, "**纯文本粘贴**");
        var textPaste = new DataObjectPastingEventArgs(textClipboard, false, DataFormats.UnicodeText);
        editor.RaiseEvent(textPaste);
        Assert(textPaste.CommandCancelled && note.Data.Markdown.Contains("<strong>纯文本粘贴</strong>", StringComparison.Ordinal),
               "the WPF plain-text paste route inserts rich content");
        var copiedNote = new DataObject(DataFormats.UnicodeText, "## 保留原样");
        editor.RaiseEvent(new DataObjectCopyingEventArgs(copiedNote, false));
        var internalPaste = new DataObjectPastingEventArgs(copiedNote, false, DataFormats.UnicodeText);
        editor.RaiseEvent(internalPaste);
        Assert(!internalPaste.CommandCancelled, "formatted content copied within Stick It keeps the native paste path");
        Invoke(note, "FinishEditing");
    }

    private sealed class OfflineKeyboard() : KeyboardDevice(InputManager.Current)
    {
        internal ModifierKeys ActiveModifiers { get; set; }

        protected override KeyStates GetKeyStatesFromSystem(Key key) => key switch
        {
            Key.LeftCtrl when ActiveModifiers.HasFlag(ModifierKeys.Control) => KeyStates.Down,
            Key.LeftShift when ActiveModifiers.HasFlag(ModifierKeys.Shift) => KeyStates.Down,
            Key.LeftAlt when ActiveModifiers.HasFlag(ModifierKeys.Alt) => KeyStates.Down,
            _ => KeyStates.None
        };
    }

    private sealed class OfflineInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null;
    }
    static T Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T result) return result;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var found=Find<T>(VisualTreeHelper.GetChild(root,i)); if(found!=null)return found; }
        return null;
    }
    static void Render(FrameworkElement root,int width,int height,string path)
    {
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); image.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(path);encoder.Save(file);
    }
}
