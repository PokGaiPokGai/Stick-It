using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shell;
using System.Windows.Threading;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

public sealed class Window2 : Window
{
    private const double FormatButtonWidth = 32;
    private static readonly ResourceDictionary FormattingToolbarStyles = new()
    {
        Source = new Uri("pack://application:,,,/Stick It;component/Assets/FormattingToolbar.xaml")
    };

    public NoteData Data { get; }
    public event Action Changed;
    internal event Action CardChanged;
    public event Action DeleteRequested;
    private readonly NoteEditor editor;
    private readonly Border paper;
    private readonly NoteChrome chrome;
    private readonly ScrollViewer footer;
    private readonly TextBlock placeholder;
    private DispatcherOperation formattingRefresh;
    private DispatcherOperation heightFit;
    private bool fittingHeight;
    private bool draggingNote;
    private bool disposing;
    private bool editing;
    private bool resumeEditingOnActivation;
    private int readingWheelDelta;
    internal bool MousePassthrough { get; private set; }

    public Window2(NoteData data)
    {
        Data=data;
        Title="Stick It";
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Stick It;component/Assets/StickIt.ico"));
        Width=data.Width; Height=data.Height; Left=data.Left; Top=data.Top;
        MinWidth=200; Topmost=true; ShowInTaskbar=false;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.CanResize;
        Background=Brushes.Transparent; AllowsTransparency=true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight=0, ResizeBorderThickness=new Thickness(5,0,5,0), GlassFrameThickness=new Thickness(0), CornerRadius=new CornerRadius(NoteChrome.Corner) });
        paper=new Border { Background=Brushes.Transparent, Padding=new Thickness(17,7,7,7) };
        paper.MouseLeftButtonDown += (_, e) => {
            if (e.ButtonState==MouseButtonState.Pressed && e.OriginalSource==paper) DragMove();
        };
        var header=new Grid { Background=Brushes.Transparent, Margin=new Thickness(9,0,6,0) };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState==MouseButtonState.Pressed) DragMove(); };
        var close = IconButton("\uE8BB", "收起便签");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Close();
        header.Children.Add(close);

        var context=new ContextMenu();
        var palette=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(4) };
        foreach (var color in NoteData.Colors)
        {
            var swatch=new Button { Width=24, Height=24, Margin=new Thickness(3), Background=(Brush)new BrushConverter().ConvertFromString(color), BorderThickness=new Thickness(0), ToolTip=color };
            swatch.Click += (_, _) => { Data.Color=color; chrome.Background=PaperBrush; Data.Modified=DateTime.Now; context.IsOpen=false; NotifyCardChanged(); };
            palette.Children.Add(swatch);
        }
        context.Items.Add(new MenuItem { Header=palette, StaysOpenOnClick=true });
        context.Items.Add(new Separator());
        var delete=new MenuItem { Header="删除便签" }; delete.Click += (_, _) => DeleteRequested?.Invoke(); context.Items.Add(delete);
        var body = new Grid();
        editor = new NoteEditor(data.Markdown) { ContextMenu = context };
        body.Children.Add(editor);
        footer = CreateFormattingToolbar();
        paper.Child = body;
        chrome = new NoteChrome(paper, header, footer) { Background = PaperBrush, ContextMenu = context };
        Content = chrome;
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (e.ExtentHeightChange != 0) QueueHeightFit();
        }));
        placeholder=new TextBlock { Text="写点什么…", Opacity=.4, IsHitTestVisible=false, Margin=new Thickness(0,2,0,0) };
        body.Children.Add(placeholder);
        UpdatePlaceholder();
        editor.DocumentEdited += () =>
        {
            SaveDocumentChanges();
            QueueFormattingRefresh();
        };
        editor.SelectionChanged += (_, _) => QueueFormattingRefresh();
        editor.GotKeyboardFocus += (_, _) => QueueFormattingRefresh();
        // Navigation can clear typing marks after SelectionChanged, even if
        // the caret is already at the beginning or end of the document.
        editor.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => QueueFormattingRefresh()), true);
        editor.PreviewMouseLeftButtonDown += (_, e) => {
            if (!editing)
            {
                for (var node=e.OriginalSource as DependencyObject; node!=null;)
                {
                    if (node is Hyperlink or CheckBox) return;
                    node=node is FrameworkContentElement content ? content.Parent :
                        node is Visual ? VisualTreeHelper.GetParent(node) : null;
                }
                if (Data.Markdown.Length != 0 && e.OriginalSource is not FrameworkContentElement &&
                    e.GetPosition(paper).Y < NoteChrome.BarHeight && editor.GetPositionFromPoint(e.GetPosition(editor), false) == null)
                {
                    e.Handled = true;
                    DragMove();
                    return;
                }
                SetEditing(true);
            }
            QueueFormattingRefresh();
        };
        editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OpenLink));
        Deactivated += (_, _) =>
        {
            var wasEditing = editing;
            if (!context.IsOpen) FinishEditing();
            resumeEditingOnActivation = wasEditing;
        };
        Activated += (_, _) =>
        {
            // The native window can reactivate with its previous keyboard focus
            // after an input-language popup or switching applications, without a click.
            if (resumeEditingOnActivation) BeginEditing();
        };
        context.Closed += (_, _) => {
            if (editing && IsActive) editor.Focus();
            else FinishEditing();
        };
        LocationChanged += (_, _) =>
        {
            if (fittingHeight || WindowState != WindowState.Normal) return;
            Data.Left = Left;
            Data.Top = Top + chrome.SlotHeight;
            Changed?.Invoke();
            QueueHeightFit();
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState != WindowState.Normal) return;
            if (e.WidthChanged) { Data.Width = e.NewSize.Width; Changed?.Invoke(); }
            QueueHeightFit();
        };
        DpiChanged += (_, _) => QueueHeightFit();
        StateChanged += (_, _) => QueueHeightFit();
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        DesktopIntegration.ConfigureNoteWindow(handle);
        HwndSource.FromHwnd(handle).AddHook(NoteWindowMessage);
        if (MousePassthrough)
            DesktopIntegration.SetNoteMousePassthrough(new WindowInteropHelper(this).Handle, true);
    }

    private IntPtr NoteWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0216) draggingNote = true; // WM_MOVING: allow crossing a monitor edge.
        if (message == 0x0232) draggingNote = false; // WM_EXITSIZEMOVE.
        if (message is 0x0232 or 0x007E or 0x001A) QueueHeightFit(); // Move/resize finished, display or work area changed.
        if (message == 0x0084 && !chrome.ContainsSurface(DesktopIntegration.PointInWindow(hwnd, lParam))) // WM_NCHITTEST
        {
            handled = true;
            return new IntPtr(-1); // HTTRANSPARENT, including the two clear strips in reading mode.
        }
        // Scroll a hovered reading note without activating its window or
        // moving keyboard focus into the editor.
        if (message == 0x020A && !editing && !MousePassthrough && chrome.ContainsSurface(DesktopIntegration.PointInWindow(hwnd, lParam)))
        {
            ScrollReadingContent(unchecked((short)(wParam.ToInt64() >> 16)));
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Handled || editing || MousePassthrough) return;
        ScrollReadingContent(e.Delta);
        e.Handled = true;
    }

    private void ScrollReadingContent(int delta)
    {
        readingWheelDelta += delta;
        var notches = readingWheelDelta / Mouse.MouseWheelDeltaForOneLine;
        readingWheelDelta %= Mouse.MouseWheelDeltaForOneLine;
        var lines = SystemParameters.WheelScrollLines;
        for (var notch = 0; notch < Math.Abs(notches); notch++)
        {
            if (lines < 0)
            {
                if (notches > 0) editor.PageUp(); else editor.PageDown();
            }
            else for (var line = 0; line < lines; line++)
            {
                if (notches > 0) editor.LineUp(); else editor.LineDown();
            }
        }
    }

    internal void SetMousePassthrough(bool enabled)
    {
        if (MousePassthrough == enabled) return;
        if (enabled) FinishEditing();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) DesktopIntegration.SetNoteMousePassthrough(handle, enabled);
        MousePassthrough = enabled;
    }

    public Brush PaperBrush => (Brush)new BrushConverter().ConvertFromString(Data.Color);
    public string PlainText => editor.PlainText;
    internal FlowDocument CreatePreview() => NotePreview.Create(editor.Document);

    private void SaveDocumentChanges()
    {
        var markdown = editor.Markdown;
        if (Data.Markdown == markdown) return;
        Data.Markdown = markdown;
        Data.Modified = DateTime.Now;
        UpdatePlaceholder();
        NotifyCardChanged();
    }

    private void NotifyCardChanged()
    {
        Changed?.Invoke();
        CardChanged?.Invoke();
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if ((!editing || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) &&
            e.Uri is { IsAbsoluteUri: true } uri && uri.Scheme is "https" or "http" or "mailto")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private ScrollViewer CreateFormattingToolbar()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        var scroller = new ScrollViewer
        {
            Content = toolbar, Margin = new Thickness(8, 0, 8, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false, Focusable = false, Background = Brushes.Transparent,
            PanningMode = PanningMode.HorizontalOnly
        };
        var scrollOffset = 0.0;
        scroller.ScrollChanged += (_, _) => scrollOffset = scroller.HorizontalOffset;
        scroller.PreviewMouseWheel += (_, e) =>
        {
            scrollOffset = Math.Clamp(scrollOffset - e.Delta / 120.0 * FormatButtonWidth, 0, scroller.ScrollableWidth);
            scroller.ScrollToHorizontalOffset(scrollOffset);
            e.Handled = true;
        };
        var buttonStyle = (Style)FormattingToolbarStyles["FormatButtonStyle"];
        void Add(string text, string hint, Func<NoteEditor.FormattingState, bool?> state,
            Action apply, InlineStyle style = InlineStyle.None)
        {
            var label = new TextBlock { Text = text, FontSize = 13 };
            if (style == InlineStyle.Bold) label.FontWeight = FontWeights.Bold;
            if (style == InlineStyle.Italic) label.FontStyle = FontStyles.Italic;
            if (style == InlineStyle.Underline) label.TextDecorations = TextDecorations.Underline;
            if (style == InlineStyle.Strike) label.TextDecorations = TextDecorations.Strikethrough;
            var button = new FormatButton(() =>
            {
                if (IsActive && !editor.IsKeyboardFocused) editor.Focus();
                apply();
            }, state) { Content = label, ToolTip = hint, Style = buttonStyle, Width = FormatButtonWidth };
            System.Windows.Automation.AutomationProperties.SetName(button, hint);
            toolbar.Children.Add(button);
        }

        Add("H", "标题 / 正文（Ctrl+Alt+1–6 / Ctrl+Alt+0）", state => state.Heading, editor.ToggleHeading);
        Add("B", "粗体（Ctrl+B）", state => state.Bold, () => editor.ToggleMark(InlineStyle.Bold), InlineStyle.Bold);
        Add("I", "斜体（Ctrl+I）", state => state.Italic, () => editor.ToggleMark(InlineStyle.Italic), InlineStyle.Italic);
        Add("U", "下划线（Ctrl+U）", state => state.Underline, () => editor.ToggleMark(InlineStyle.Underline), InlineStyle.Underline);
        Add("S", "删除线（Ctrl+Shift+X）", state => state.Strike, () => editor.ToggleMark(InlineStyle.Strike), InlineStyle.Strike);
        Add("•≡", "项目符号列表（Ctrl+Shift+8）", state => state.Bullets, () => editor.ToggleList(false));
        Add("1.", "编号列表（Ctrl+Shift+7）", state => state.Numbers, () => editor.ToggleList(true));
        Add("☑", "待办事项（Ctrl+Shift+9）", state => state.Tasks, editor.ToggleTask);
        Add("</>", "行内代码", state => state.Code, () => editor.ToggleMark(InlineStyle.Code));
        return scroller;
    }

    private void QueueFormattingRefresh()
    {
        if (!editing || formattingRefresh != null) return;
        formattingRefresh = Dispatcher.InvokeAsync(() =>
        {
            formattingRefresh = null;
            if (editing) RefreshFormattingToolbar();
        }, DispatcherPriority.Input);
    }

    private void RefreshFormattingToolbar()
    {
        var formatting = editor.ReadFormatting();
        foreach (FormatButton button in ((StackPanel)footer.Content).Children) button.UpdateState(formatting);
    }

    private sealed class FormatButton(Action apply, Func<NoteEditor.FormattingState, bool?> state) : ToggleButton
    {
        internal void UpdateState(NoteEditor.FormattingState formatting) => IsChecked = state(formatting);
        protected override void OnToggle() => apply();
    }

    private static Button IconButton(string glyph, string hint) => new Button {
        Content=glyph, ToolTip=hint, FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=12,
        Width=28, Height=NoteChrome.BarHeight, Background=Brushes.Transparent, BorderThickness=new Thickness(0), Focusable=false,
        Template=FlatButtonTemplate()
    };
    public static ControlTemplate FlatButtonTemplate()
    {
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)) { VisualTree=border };
        var hover=new Trigger { Property=IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(BackgroundProperty,new SolidColorBrush(Color.FromArgb(18,0,0,0)))); template.Triggers.Add(hover);
        return template;
    }
    internal double MeasurePaperHeight(double width, double availableHeight)
    {
        // Bound the first layout as well: a long note only needs one screen.
        paper.Measure(new Size(width, availableHeight));
        return paper.DesiredSize.Height;
    }

    private bool CanFit => IsVisible && !disposing && !draggingNote && WindowState == WindowState.Normal;

    private void QueueHeightFit()
    {
        if (!CanFit || fittingHeight || heightFit != null) return;
        heightFit = Dispatcher.InvokeAsync(() =>
        {
            heightFit = null;
            if (CanFit && editor.ExtentHeight > 0) FitContentHeight();
        }, DispatcherPriority.Loaded);
    }

    private void FitContentHeight(bool opening = false)
    {
        // Creating the first HWND also raises LocationChanged. Its provisional
        // position must not replace the stored text anchor during restoration.
        fittingHeight = true;
        try
        {
            var handle = new WindowInteropHelper(this).EnsureHandle();
            var display = DesktopIntegration.GetNoteDisplay(handle);
            chrome.SetSlotHeight(display.BarPixels / display.Scale);
            var desired = opening ? MeasurePaperHeight(display.Width, display.AvailablePaperHeight)
                : editor.ExtentHeight + paper.Padding.Top + paper.Padding.Bottom;
            var preferredTop = display.Bounds.Top + (int)Math.Round((Data.Top - Top) * display.Scale);
            var fitted = display.FitWindow(desired, preferredTop);
            if (opening) fitted = DesktopIntegration.VisibleBounds(fitted, display.Work);
            editor.SetValue(NoteEditor.FitsWorkAreaProperty, desired < display.AvailablePaperHeight);
            if (!fitted.Equals(display.Bounds)) DesktopIntegration.SetNoteBounds(handle, fitted);
            var readingHeight = (fitted.Bottom - fitted.Top - 2 * display.BarPixels) / display.Scale;
            if (Data.Height != readingHeight) { Data.Height = readingHeight; Changed?.Invoke(); }
        }
        finally { fittingHeight = false; }
    }

    public void Open(bool edit = false, bool activate = true, bool atCursor = false)
    {
        if (activate) SetMousePassthrough(false);
        Data.IsOpen = true;
        ShowActivated = activate;
        if (!IsVisible)
        {
            SetEditing(edit, animate:false);
            if (atCursor) DesktopIntegration.PlaceAtCursor(this);
            else FitContentHeight(opening:true);
        }
        Show();
        WindowState = WindowState.Normal;
        if (activate) Activate();
        if (edit) { BeginEditing(); editor.Focus(); }
        QueueHeightFit();
        Changed?.Invoke();
    }
    private void UpdatePlaceholder() => placeholder.Visibility=!editing && Data.Markdown.Length==0 ? Visibility.Visible : Visibility.Collapsed;
    private void BeginEditing() => SetEditing(true);
    private void FinishEditing() => SetEditing(false);
    private void SetEditing(bool value, bool animate = true)
    {
        resumeEditingOnActivation=false;
        if (editing == value && animate) return;
        editing = value;
        editor.IsReadOnly = !value;
        if (value) editor.ClearValue(ContextMenuProperty);
        else editor.ContextMenu = chrome.ContextMenu;
        chrome.SetEditing(value, animate && IsVisible && SystemParameters.ClientAreaAnimation);
        UpdatePlaceholder();
        if (value) QueueFormattingRefresh();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!disposing) { e.Cancel=true; SetEditing(false, animate:false); Data.IsOpen=false; Hide(); Changed?.Invoke(); }
        base.OnClosing(e);
    }
    internal void PrepareForShutdown() => disposing = true;
    public void DisposeWindow() { PrepareForShutdown(); Close(); }
}
