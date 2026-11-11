using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shell;
using static StickyNotesDemo.MarkdownView;

namespace StickyNotesDemo;

public sealed class Window2 : Window
{
    private const double HeaderHeight = 28;
    private const double FooterHeight = 34;

    public NoteData Data { get; }
    public event Action Changed;
    public event Action DeleteRequested;
    private readonly NoteEditor editor;
    private readonly Border paper;
    private readonly Grid header;
    private readonly UniformGrid footer;
    private readonly TextBlock placeholder;
    private bool disposing;
    private bool editing;

    public Window2(NoteData data)
    {
        Data=data;
        Title="Stick It";
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Stick It;component/Assets/StickIt.ico"));
        Width=data.Width; Height=data.Height; Left=data.Left; Top=data.Top;
        MinWidth=200; MinHeight=160; Topmost=true; ShowInTaskbar=false;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.CanResize;
        Background=Brushes.Transparent; AllowsTransparency=true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight=0, ResizeBorderThickness=new Thickness(5), GlassFrameThickness=new Thickness(0), CornerRadius=new CornerRadius(10) });
        paper=new Border { CornerRadius=new CornerRadius(10), Background=PaperBrush, Padding=new Thickness(1) };
        Content=paper;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(FooterHeight) });
        paper.Child=layout;
        paper.MouseLeftButtonDown += (_, e) => {
            if (e.ButtonState==MouseButtonState.Pressed && (e.OriginalSource==paper || e.OriginalSource==layout)) DragMove();
        };
        header=new Grid { Background=Brushes.Transparent, Margin=new Thickness(9,0,6,0), Visibility=Visibility.Collapsed };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState==MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(header);
        var close = IconButton("\uE8BB", "收起便签");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Close();
        header.Children.Add(close);

        var context=new ContextMenu();
        paper.ContextMenu = context;
        var palette=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(4) };
        foreach (var color in new[] { "#FFF7C2", "#E2F1FF", "#F5E0F0", "#E4F2D5", "#EEE8FB", "#F0F0F0" })
        {
            var swatch=new Button { Width=24, Height=24, Margin=new Thickness(3), Background=(Brush)new BrushConverter().ConvertFromString(color), BorderThickness=new Thickness(0), ToolTip=color };
            swatch.Click += (_, _) => { Data.Color=color; paper.Background=PaperBrush; Data.Modified=DateTime.Now; context.IsOpen=false; Changed?.Invoke(); };
            palette.Children.Add(swatch);
        }
        context.Items.Add(new MenuItem { Header=palette, StaysOpenOnClick=true });
        context.Items.Add(new Separator());
        var delete=new MenuItem { Header="删除便签" }; delete.Click += (_, _) => DeleteRequested?.Invoke(); context.Items.Add(delete);
        var body = new Grid { Margin = new Thickness(16, 2, 16, 14) };
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        editor = new NoteEditor(data.Markdown) { ContextMenu = context };
        body.Children.Add(editor);
        footer = CreateFormattingToolbar();
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        placeholder=new TextBlock { Text="写点什么…", Opacity=.4, IsHitTestVisible=false, Margin=new Thickness(0,2,0,0) };
        body.Children.Add(placeholder);
        UpdatePlaceholder();
        editor.DocumentEdited += SaveDocumentChanges;
        editor.PreviewMouseLeftButtonDown += (_, e) => {
            if (editing) return;
            for (var node=e.OriginalSource as DependencyObject; node!=null;)
            {
                if (node is Hyperlink or CheckBox) return;
                node=node is FrameworkContentElement content ? content.Parent :
                    node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            BeginEditing();
        };
        editor.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OpenLink));
        Deactivated += (_, _) => { if (!context.IsOpen) FinishEditing(); };
        context.Closed += (_, _) => {
            if (editing && IsActive) editor.Focus();
            else FinishEditing();
        };
        LocationChanged += (_, _) => { Data.Left=Left; Data.Top=Top; Changed?.Invoke(); };
        SizeChanged += (_, _) => { Data.Width=Width; Data.Height=Height; Changed?.Invoke(); };
    }
    public Brush PaperBrush => (Brush)new BrushConverter().ConvertFromString(Data.Color);
    public string PlainText => editor.PlainText;

    private void SaveDocumentChanges()
    {
        var markdown = editor.Markdown;
        if (Data.Markdown == markdown) return;
        Data.Markdown = markdown;
        Data.Modified = DateTime.Now;
        UpdatePlaceholder();
        Changed?.Invoke();
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if ((!editing || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) &&
            e.Uri is { IsAbsoluteUri: true } uri && uri.Scheme is "https" or "http" or "mailto")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private UniformGrid CreateFormattingToolbar()
    {
        var toolbar = new UniformGrid { Rows = 1, Margin = new Thickness(8, 0, 8, 4), Visibility = Visibility.Collapsed };
        void Add(string text, string hint, Action apply, InlineStyle style = InlineStyle.None)
        {
            var label = new TextBlock { Text = text, FontSize = 13 };
            if (style == InlineStyle.Bold) label.FontWeight = FontWeights.Bold;
            if (style == InlineStyle.Italic) label.FontStyle = FontStyles.Italic;
            if (style == InlineStyle.Underline) label.TextDecorations = TextDecorations.Underline;
            if (style == InlineStyle.Strike) label.TextDecorations = TextDecorations.Strikethrough;
            var button = new Button
            {
                Content = label, ToolTip = hint, Focusable = false, Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(65, 65, 65)), Template = FlatButtonTemplate()
            };
            System.Windows.Automation.AutomationProperties.SetName(button, hint);
            button.Click += (_, _) =>
            {
                editor.Focus();
                apply();
            };
            toolbar.Children.Add(button);
        }

        Add("H", "标题 / 正文（Ctrl+Alt+1–6 / Ctrl+Alt+0）", editor.ToggleHeading);
        Add("B", "粗体（Ctrl+B）", () => editor.ToggleMark(InlineStyle.Bold), InlineStyle.Bold);
        Add("I", "斜体（Ctrl+I）", () => editor.ToggleMark(InlineStyle.Italic), InlineStyle.Italic);
        Add("U", "下划线（Ctrl+U）", () => editor.ToggleMark(InlineStyle.Underline), InlineStyle.Underline);
        Add("S", "删除线（Ctrl+Shift+X）", () => editor.ToggleMark(InlineStyle.Strike), InlineStyle.Strike);
        Add("•≡", "项目符号列表（Ctrl+Shift+8）", () => editor.ToggleList(false));
        Add("1.", "编号列表（Ctrl+Shift+7）", () => editor.ToggleList(true));
        Add("☑", "待办事项（Ctrl+Shift+9）", editor.ToggleTask);
        Add("</>", "行内代码", () => editor.ToggleMark(InlineStyle.Code));
        return toolbar;
    }

    private static Button IconButton(string glyph, string hint) => new Button {
        Content=glyph, ToolTip=hint, FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=12,
        Width=28, Height=HeaderHeight, Background=Brushes.Transparent, BorderThickness=new Thickness(0), Focusable=false,
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
    public void Open(bool edit=false)
    {
        Data.IsOpen=true; Show(); WindowState=WindowState.Normal; Activate();
        if (edit) { BeginEditing(); editor.Focus(); }
        Changed?.Invoke();
    }
    private void UpdatePlaceholder() => placeholder.Visibility=!editing && Data.Markdown.Length==0 ? Visibility.Visible : Visibility.Collapsed;
    private void BeginEditing()
    {
        editing=true;
        header.Visibility=Visibility.Visible;
        footer.Visibility = Visibility.Visible;
        editor.IsReadOnly = false;
        editor.ClearValue(ContextMenuProperty);
        UpdatePlaceholder();
    }
    private void FinishEditing()
    {
        if (!editing) return;
        editing=false;
        header.Visibility=Visibility.Collapsed;
        footer.Visibility = Visibility.Collapsed;
        editor.IsReadOnly = true;
        editor.ContextMenu = paper.ContextMenu;
        UpdatePlaceholder();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!disposing) { e.Cancel=true; FinishEditing(); Data.IsOpen=false; Hide(); Changed?.Invoke(); }
        base.OnClosing(e);
    }
    public void DisposeWindow() { disposing=true; Close(); }
}
