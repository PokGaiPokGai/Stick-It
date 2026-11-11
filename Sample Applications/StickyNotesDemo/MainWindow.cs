using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotesDemo;

public partial class MainWindow : Window
{
    private readonly NoteStore store;
    private readonly List<Window2> notes=new();
    private readonly DispatcherTimer saveTimer;
    private System.Windows.Forms.NotifyIcon tray;
    private System.Windows.Forms.ContextMenuStrip trayMenu;
    private System.Drawing.Icon trayArtwork;
    private HwndSource source;
    private bool hotkeyRegistered;
    private bool exiting;

    public MainWindow(NoteStore store)
    {
        this.store=store;
        InitializeComponent();
        saveTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(600) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
    }
    public void Start()
    {
        foreach (var data in store.Load()) AddNote(data);
        trayArtwork = LoadTrayArtwork();
        trayMenu=new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开便签列表",null,(_,_)=>ShowLibrary());
        trayMenu.Items.Add("新建便签    Shift + Alt + W",null,(_,_)=>CreateNote());
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("保存并退出",null,(_,_)=>ExitApp());
        tray=new System.Windows.Forms.NotifyIcon { Text="Stick It", Icon=trayArtwork, ContextMenuStrip=trayMenu, Visible=true };
        tray.MouseClick += (_,e)=> { if(e.Button==System.Windows.Forms.MouseButtons.Left) ShowLibrary(); };
        ShowLibrary();
        foreach (var note in notes.Where(n=>n.Data.IsOpen)) note.Open();
        RefreshList();
    }

    private static System.Drawing.Icon LoadTrayArtwork()
    {
        using var stream = Application.GetResourceStream(
            new Uri("pack://application:,,,/Stick It;component/Assets/StickIt.ico")).Stream;
        // NotifyIcon submits this handle directly to Windows, so select the small frame here.
        return new System.Drawing.Icon(stream, DesktopIntegration.TrayIconSize());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(WindowMessage);
        hotkeyRegistered=DesktopIntegration.RegisterHotKey(source.Handle,DesktopIntegration.HotkeyId,DesktopIntegration.HotkeyModifiers,DesktopIntegration.HotkeyKey);
        if(!hotkeyRegistered) Report("Shift + Alt + W 注册失败，可能已被其他程序占用。仍可用 + 或托盘新建便签。");
    }
    private IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==0x0312 && wParam.ToInt32()==DesktopIntegration.HotkeyId) { CreateNote(); handled=true; }
        return IntPtr.Zero;
    }
    private Window2 AddNote(NoteData data)
    {
        var note=new Window2(data); notes.Add(note);
        note.Changed+=OnNoteChanged;
        note.DeleteRequested+=()=>DeleteNote(note);
        return note;
    }
    private void CreateNote()
    {
        var note=AddNote(new NoteData { Pinned=true });
        DesktopIntegration.PlaceAtCursor(note);
        note.Open(edit:true);
        OnNoteChanged();
    }
    private void OnNoteChanged()
    {
        if(exiting) return;
        RefreshList(); saveTimer.Stop(); saveTimer.Start();
    }
    public void ShowLibrary() { ShowInTaskbar=true; Show(); WindowState=WindowState.Normal; Activate(); RefreshList(); }
    private void RefreshList()
    {
        if(NoteList==null) return;
        var query=SearchBox.Text.Trim();
        var rows=notes.Where(n=>n.Data.Markdown.Contains(query,StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(n=>n.Data.Modified).Select(n=>new NoteCard(n)).ToArray();
        NoteList.ItemsSource=rows;
        SearchHint.Visibility=query.Length==0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility=rows.Length==0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text=query.Length==0 ? "还没有便签" : "没有找到匹配的便签";
    }
    private bool Save()
    {
        try { store.Save(notes.Select(n=>n.Data).ToList()); return true; }
        catch(Exception error) when(error is IOException || error is UnauthorizedAccessException)
        { Report("便签未能保存："+error.Message); return false; }
    }
    private void Report(string message) { StatusText.Text=message; StatusText.Visibility=Visibility.Visible; }
    private void DeleteNote(Window2 note)
    {
        if(MessageBox.Show("删除这张便签？", "Stick It", MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
        notes.Remove(note); note.DisposeWindow(); OnNoteChanged();
    }
    private void ExitApp()
    {
        saveTimer.Stop();
        if(!Save()) { ShowLibrary(); return; }
        exiting=true;
        if(hotkeyRegistered) DesktopIntegration.UnregisterHotKey(source.Handle,DesktopIntegration.HotkeyId);
        source?.RemoveHook(WindowMessage);
        tray.Dispose(); trayMenu.Dispose(); trayArtwork.Dispose();
        foreach(var note in notes) note.DisposeWindow();
        Close(); Application.Current.Shutdown();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(!exiting) { e.Cancel=true; if(Save()) { Hide(); ShowInTaskbar=false; } }
        base.OnClosing(e);
    }
    private void DragTitle(object sender,MouseButtonEventArgs e) { if(e.ButtonState==MouseButtonState.Pressed) DragMove(); }
    private void NewNoteClick(object sender,RoutedEventArgs e)=>CreateNote();
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private void SettingsClick(object sender,RoutedEventArgs e) { var button=(Button)sender; button.ContextMenu.PlacementTarget=button; button.ContextMenu.IsOpen=true; }
    private void SearchChanged(object sender,TextChangedEventArgs e)=>RefreshList();
    private void OpenCard(object sender,RoutedEventArgs e)=>((Window2)((Button)sender).Tag).Open(edit:true);
    private void DeleteCard(object sender,RoutedEventArgs e) { var menu=(ContextMenu)((MenuItem)sender).Parent; DeleteNote((Window2)((Button)menu.PlacementTarget).Tag); }
    private void SaveClick(object sender,RoutedEventArgs e)=>Save();
    private void ExitClick(object sender,RoutedEventArgs e)=>ExitApp();
}

public sealed class NoteCard
{
    public Window2 Note { get; }
    public Brush Background=>Note.PaperBrush;
    public string Time=>Note.Data.Modified.ToString("H:mm");
    public string Title { get; }
    public string Body { get; }
    public NoteCard(Window2 note)
    {
        Note=note;
        var text=Markdig.Markdown.ToPlainText(note.Data.Markdown).Trim();
        var newline=text.IndexOf('\n');
        Title=text.Length==0 ? "空白便签" : newline<0 ? text : text.Substring(0,newline).TrimEnd();
        Body=newline<0 ? "" : text.Substring(newline+1).Trim();
    }
}
