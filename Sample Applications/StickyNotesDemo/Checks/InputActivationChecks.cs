using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;

internal static partial class Checks
{
    private static void TestInputActivation()
    {
        var foreground = GetForegroundWindow();
        var session = new EditorSession("已有内容");
        try
        {
            void Activate(bool active) => typeof(Window)
                .GetMethod(active ? "OnActivated" : "OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(session.Window, [EventArgs.Empty]);
            session.End(session.Body());
            var document = session.Editor.Document;
            Activate(false);
            Assert(session.Editor.IsReadOnly, "leaving an editing note still hides its editing state");
            session.Type("不应输入");
            Assert(session.Markdown == "已有内容", "an inactive note rejects text while it is in reading mode");
            Assert(NativeInputIsReadOnly(session.Editor), "the TSF input context reports reading notes as read-only");
            Activate(true);
            session.Type("中文");
            Assert(!session.Editor.IsReadOnly && session.Markdown == "已有内容中文" && ReferenceEquals(document, session.Editor.Document),
                "returning to a previously editing note accepts committed Chinese text at the existing caret");
            Assert(!NativeInputIsReadOnly(session.Editor), "the TSF input context reports a reactivated editing note as writable");
            session.Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            var switchKey = new KeyEventArgs(offlineKeyboard, new OfflineInputSource { RootVisual = session.Editor }, Environment.TickCount, Key.Space)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            offlineKeyboard.ActiveModifiers = ModifierKeys.Control;
            try
            {
                session.Editor.RaiseEvent(switchKey);
                if (!switchKey.Handled)
                {
                    switchKey.RoutedEvent = Keyboard.KeyDownEvent;
                    session.Editor.RaiseEvent(switchKey);
                }
            }
            finally { offlineKeyboard.ActiveModifiers = ModifierKeys.None; }
            Assert(!switchKey.Handled && !session.Editor.IsReadOnly && session.Markdown == "已有内容中文" &&
                   session.Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold),
                "Ctrl+Space is left unhandled for the input method and preserves text, formatting and editing state");
            session.Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            Activate(false);
            Activate(true);
            session.Type(" English");
            Assert(session.Markdown == "已有内容中文 English", "repeated activation changes preserve Chinese and English input in the same document");

            Invoke(session.Window, "FinishEditing");
            Activate(false);
            Activate(true);
            Assert(session.Editor.IsReadOnly, "activating an originally read-only note does not start editing");

            Invoke(session.Window, "BeginEditing");
            Activate(false);
            session.Window.SetMousePassthrough(true);
            Activate(true);
            Assert(session.Editor.IsReadOnly && session.Window.MousePassthrough,
                "game passthrough cancels pending editing restoration");
            session.Window.SetMousePassthrough(false);

            Invoke(session.Window, "BeginEditing");
            Activate(false);
            session.Window.Close();
            Activate(true);
            Assert(session.Editor.IsReadOnly && !session.Window.Data.IsOpen,
                "closing a suspended note cancels editing restoration without reopening it");
            Assert(!session.Window.IsVisible && GetForegroundWindow() == foreground,
                "activation-event replay neither shows a window nor changes desktop focus or input language");
        }
        finally { session.Window.DisposeWindow(); }
    }

    private static bool NativeInputIsReadOnly(TextBoxBase editor)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var textEditor = typeof(TextBoxBase).GetProperty("TextEditor", instance).GetValue(editor);
        var textStore = textEditor.GetType().GetProperty("TextStore", instance).GetValue(textEditor);
        var managerProperty = textStore.GetType().GetProperty("DocumentManager", instance);
        var manager = managerProperty.GetValue(textStore);
        var getContext = managerProperty.PropertyType.GetMethod("GetBase");
        var contextArgs = new object[] { null };
        getContext.Invoke(manager, contextArgs);
        var context = contextArgs[0];
        try
        {
            ((InputContextStatus)context).GetStatus(out var status);
            return (status.DynamicFlags & 1) != 0; // TF_SD_READONLY
        }
        finally { Marshal.ReleaseComObject(context); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputStatus { public uint DynamicFlags, StaticFlags; }

    // ITfContext's slot order from the Windows SDK msctf.h. Only GetStatus is invoked.
    [ComImport, Guid("aa80e7fd-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface InputContextStatus
    {
        void RequestEditSession();
        void InWriteSession();
        void GetSelection();
        void SetSelection();
        void GetStart();
        void GetEnd();
        void GetActiveView();
        void EnumViews();
        void GetStatus(out InputStatus status);
    }
}
