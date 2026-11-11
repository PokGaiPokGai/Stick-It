using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace StickyNotesDemo;

public sealed class Window2 : Window
{
    public NoteData Data { get; }
    public event Action Changed;
    public event Action DeleteRequested;
    private readonly TextBox editor;
    private readonly FlowDocumentScrollViewer rendered;
    private readonly Border paper;
    private readonly Grid header;
    private readonly Grid body;
    private readonly TextBlock placeholder;
    private readonly Button pin;
    private bool disposing;
    private bool editing;

    public Window2(NoteData data)
    {
        Data=data;
        Title="Stick It";
        Icon=BitmapFrame.Create(new Uri("pack://application:,,,/Stick It;component/Assets/StickIt.ico"));
        Width=data.Width; Height=data.Height; Left=data.Left; Top=data.Top;
        MinWidth=200; MinHeight=160; Topmost=data.Pinned; ShowInTaskbar=false;
        WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.CanResize;
        Background=Brushes.Transparent; AllowsTransparency=true;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight=0, ResizeBorderThickness=new Thickness(5), GlassFrameThickness=new Thickness(0), CornerRadius=new CornerRadius(10) });
        paper=new Border { CornerRadius=new CornerRadius(10), Background=PaperBrush, Padding=new Thickness(1) };
        Content=paper;
        var layout=new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        paper.Child=layout;
        paper.MouseLeftButtonDown += (_, e) => {
            if (e.ButtonState==MouseButtonState.Pressed && (e.OriginalSource==paper || e.OriginalSource==layout)) DragMove();
        };
        header=new Grid { Background=Brushes.Transparent, Margin=new Thickness(9,0,6,0), Height=34, Visibility=Visibility.Collapsed };
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState==MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(header);
        var grip=new TextBlock { Text="···", Foreground=new SolidColorBrush(Color.FromArgb(80,0,0,0)), VerticalAlignment=VerticalAlignment.Center, FontSize=18 };
        header.Children.Add(grip);
        var actions=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
        header.Children.Add(actions);
        pin=IconButton("\uE718", "置顶"); pin.Opacity=data.Pinned ? .8 : .35;
        pin.Click += (_, _) => { Data.Pinned=Topmost=!Topmost; pin.Opacity=Topmost ? .8 : .35; Changed?.Invoke(); };
        actions.Children.Add(pin);
        var menu=IconButton("\uE712", "颜色与删除");
        var context=new ContextMenu();
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
        menu.Click += (_, _) => { context.PlacementTarget=menu; context.IsOpen=true; }; actions.Children.Add(menu);
        var close=IconButton("\uE8BB", "收起便签"); close.Click += (_, _) => Close(); actions.Children.Add(close);
        body=new Grid { Margin=new Thickness(16,16,16,14) }; Grid.SetRow(body,1); layout.Children.Add(body);
        editor=new TextBox { Text=data.Markdown, AcceptsReturn=true, AcceptsTab=true, TextWrapping=TextWrapping.Wrap,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Background=Brushes.Transparent, BorderThickness=new Thickness(0),
            Padding=new Thickness(0), FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize=14, Foreground=new SolidColorBrush(Color.FromRgb(38,38,38)),
            Visibility=Visibility.Collapsed };
        rendered=new FlowDocumentScrollViewer { Document=MarkdownView.Render(data.Markdown), IsToolBarVisible=false,
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Background=Brushes.Transparent, BorderThickness=new Thickness(0), Padding=new Thickness(0) };
        body.Children.Add(rendered); body.Children.Add(editor);
        placeholder=new TextBlock { Text="写点什么…", Opacity=.4, IsHitTestVisible=false, Margin=new Thickness(0,2,0,0) };
        body.Children.Add(placeholder);
        UpdatePlaceholder();
        editor.TextChanged += (_, _) => { Data.Markdown=editor.Text; Data.Modified=DateTime.Now; UpdatePlaceholder(); Changed?.Invoke(); };
        rendered.PreviewMouseLeftButtonDown += (_, e) => {
            // Links remain clickable; clicking ordinary paper starts editing.
            for (var node=e.OriginalSource as DependencyObject; node!=null;)
            {
                if (node is Hyperlink) return;
                node=node is FrameworkContentElement content ? content.Parent :
                    node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            BeginEditing(); e.Handled=true;
        };
        editor.LostKeyboardFocus += (_, _) => { if (!context.IsOpen) FinishEditing(); };
        Deactivated += (_, _) => { if (!context.IsOpen) FinishEditing(); };
        context.Closed += (_, _) => {
            if (editing && IsActive) editor.Focus();
            else FinishEditing();
        };
        LocationChanged += (_, _) => { Data.Left=Left; Data.Top=Top; Changed?.Invoke(); };
        SizeChanged += (_, _) => { Data.Width=Width; Data.Height=Height; Changed?.Invoke(); };
    }
    public Brush PaperBrush => (Brush)new BrushConverter().ConvertFromString(Data.Color);
    private static Button IconButton(string glyph, string hint) => new Button {
        Content=glyph, ToolTip=hint, FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=12,
        Width=28, Height=28, Background=Brushes.Transparent, BorderThickness=new Thickness(0), Focusable=false,
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
        if (edit) BeginEditing();
        Changed?.Invoke();
    }
    private void UpdatePlaceholder() => placeholder.Visibility=!editing && editor.Text.Length==0 ? Visibility.Visible : Visibility.Collapsed;
    private void BeginEditing()
    {
        editing=true;
        header.Visibility=Visibility.Visible;
        body.Margin=new Thickness(16,2,16,14);
        rendered.Visibility=Visibility.Collapsed; editor.Visibility=Visibility.Visible;
        UpdatePlaceholder();
        editor.Focus(); editor.CaretIndex=editor.Text.Length;
    }
    private void FinishEditing()
    {
        if (!editing) return;
        editing=false;
        header.Visibility=Visibility.Collapsed;
        body.Margin=new Thickness(16,16,16,14);
        rendered.Document=MarkdownView.Render(editor.Text);
        editor.Visibility=Visibility.Collapsed; rendered.Visibility=Visibility.Visible;
        UpdatePlaceholder();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!disposing) { e.Cancel=true; FinishEditing(); Data.IsOpen=false; Hide(); Changed?.Invoke(); }
        base.OnClosing(e);
    }
    public void DisposeWindow() { disposing=true; Close(); }
}
