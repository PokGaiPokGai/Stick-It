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
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StickyNotesDemo;

internal static class Checks
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

            TestStore(temp); TestMarkdown(); TestWindows(temp,output);
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
        Assert(doc.Blocks.OfType<System.Windows.Documents.List>().Any(),"Markdown task list renders");
        Assert(doc.Blocks.OfType<Table>().Any(),"Markdown table renders");
        var text=new TextRange(doc.ContentStart,doc.ContentEnd).Text;
        Assert(text.Contains("☑")&&text.Contains("var x = 1;")&&!text.Contains("**bold**"),"task marks and fenced code render without syntax delimiters");
    }
    static object Invoke(object obj,string method,params object[] values) => obj.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,values);
    static void TestWindows(string temp,string output)
    {
        var main=new MainWindow(new NoteStore(Path.Combine(temp,"ui")));
        Application.Current.MainWindow=main;
        var data=new NoteData { Markdown="## Blender HairMesh 插件问题\n- 数值恢复默认值\n- 保存当前面板状态\n- 检查 UI 逻辑\n- 调整 crown 卡片",Color="#E2F1FF",Modified=DateTime.Today.AddHours(15).AddMinutes(29) };
        var note=(Window2)Invoke(main,"AddNote",data);
        Invoke(main,"AddNote",new NoteData { Markdown="fork 一下 HairMesh 仓库，自行优化",Modified=DateTime.Today.AddHours(3).AddMinutes(34) });
        Invoke(main,"AddNote",new NoteData { Markdown="随手记录一个想法",Modified=DateTime.Today.AddHours(2).AddMinutes(43) });
        Invoke(main,"RefreshList");
        Assert(!note.ShowInTaskbar && main.ShowInTaskbar,"only library window participates in taskbar");
        Assert(note.Topmost && note.Background==Brushes.Transparent && ((Border)note.Content).Background is SolidColorBrush,"pinned note uses solid rounded paper");
        Render((FrameworkElement)main.Content,478,720,Path.Combine(output,"library-preview.png"));
        Render((FrameworkElement)note.Content,320,300,Path.Combine(output,"note-preview.png"));
        var search=(TextBox)main.FindName("SearchBox"); search.Text="fork";
        var list=(ItemsControl)main.FindName("NoteList");
        Assert(list.Items.Count==1,"search filters library cards");
        var editor=Find<TextBox>((DependencyObject)note.Content);
        editor.Text="# 自动渲染\n\n**编辑完成**";
        Invoke(note,"BeginEditing"); Invoke(note,"FinishEditing");
        var view=Find<FlowDocumentScrollViewer>((DependencyObject)note.Content);
        Assert(editor.Visibility==Visibility.Collapsed&&view.Visibility==Visibility.Visible&&new TextRange(view.Document.ContentStart,view.Document.ContentEnd).Text.Contains("编辑完成"),"editing finishes into Markdown rendering without mode button");
        var close=new CancelEventArgs(); Invoke(note,"OnClosing",close);
        Assert(close.Cancel&&!data.IsOpen,"closing note hides it and keeps data");
        var libraryClose=new CancelEventArgs(); Invoke(main,"OnClosing",libraryClose);
        Assert(libraryClose.Cancel&&!main.ShowInTaskbar,"closing library removes taskbar entry without exiting");
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
