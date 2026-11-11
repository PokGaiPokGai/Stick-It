using System;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace StickyNotesDemo;
public partial class App : Application
{
    private SingleInstance instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance=new SingleInstance(@"Local\StickIt." + WindowsIdentity.GetCurrent().User.Value);
        if (!instance.IsPrimary) { Shutdown(); return; }
        base.OnStartup(e);
        try
        {
            var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stick It");
            var controller=new MainWindow(new NoteStore(directory));
            MainWindow=controller;
            instance.Listen(() => Dispatcher.BeginInvoke(new Action(controller.ShowLibrary)));
            controller.Start();
        }
        catch (Exception error)
        {
            MessageBox.Show("Stick It 无法启动，原便签文件未删除。\n" + error.Message, "Stick It", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
