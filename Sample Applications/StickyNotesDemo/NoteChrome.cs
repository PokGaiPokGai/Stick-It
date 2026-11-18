using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StickyNotesDemo;

internal sealed class NoteChrome : Grid
{
    internal const double BarHeight = 28;
    private const double PaperInset = 6;
    private const double Travel = BarHeight - PaperInset;
    private static readonly PropertyPath SlideY = new("(0).(1)", RenderTransformProperty, TranslateTransform.YProperty);
    private readonly Grid body, upper, lower;
    private readonly RichTextBox editor;
    private readonly FrameworkElement header, footer;
    private Storyboard transition;

    internal NoteChrome(Grid body, RichTextBox editor, FrameworkElement header, FrameworkElement footer)
    {
        this.body = body;
        this.editor = editor;
        this.header = header;
        this.footer = footer;
        ClipToBounds = true;

        // Two full-size clips move opposite edges of the viewport. Only their
        // intersection is visible; neither the clips nor the text resize per frame.
        upper = new Grid { Margin = new Thickness(0, PaperInset, 0, PaperInset), ClipToBounds = true, RenderTransform = new TranslateTransform() };
        lower = new Grid { ClipToBounds = true, RenderTransform = new TranslateTransform() };
        body.RenderTransform = new TranslateTransform();
        body.Margin = BodyMargin(0);
        lower.Children.Add(body);
        upper.Children.Add(lower);
        Children.Add(upper);
        header.RenderTransform = new TranslateTransform(0, -BarHeight);
        footer.RenderTransform = new TranslateTransform(0, BarHeight);
        header.VerticalAlignment = VerticalAlignment.Top;
        footer.VerticalAlignment = VerticalAlignment.Bottom;
        header.Height = footer.Height = BarHeight;
        header.Visibility = footer.Visibility = Visibility.Collapsed;
        Children.Add(header);
        Children.Add(footer);
    }

    // The last ten units of the right paper margin contain the scrollbar gutter.
    private static Thickness BodyMargin(double bottom) => new(16, 0, 6, bottom);

    internal void SetEditing(bool editing, bool animate, double? clickedLineBottom = null)
    {
        double from = ((TranslateTransform)upper.RenderTransform).Y, fromText = ((TranslateTransform)body.RenderTransform).Y;
        double to = editing ? Travel : 0;
        CancelTransition();
        header.IsHitTestVisible = footer.IsHitTestVisible = editing;
        if (editing) header.Visibility = footer.Visibility = Visibility.Visible;

        // Use the larger viewport throughout the slide so newly exposed lines
        // are already drawn. At the document end WPF may clamp the old offset.
        if (body.Margin.Bottom != 0)
        {
            var oldOffset = editor.VerticalOffset;
            body.Margin = BodyMargin(0);
            UpdateLayout();
            fromText += editor.VerticalOffset - oldOffset;
        }

        double scrollBy = 0;
        if (editing)
        {
            if (editor.VerticalOffset > 0 && editor.ExtentHeight - editor.ViewportHeight - editor.VerticalOffset < 0.5)
                scrollBy = 2 * Travel;
            else if (clickedLineBottom is double lineBottom)
                scrollBy = Math.Clamp(lineBottom - (editor.ViewportHeight - 2 * Travel), 0, 2 * Travel);
        }

        void Finish()
        {
            var offset = editor.VerticalOffset;
            body.Margin = BodyMargin(2 * to);
            Position(null, to, to, 2 * to, 2 * to);
            if (scrollBy > 0) editor.ScrollToVerticalOffset(offset + scrollBy);
            if (!editing) header.Visibility = footer.Visibility = Visibility.Collapsed;
        }

        if (!animate || from == to)
        {
            Finish();
            return;
        }

        var slide = new Storyboard
        {
            Duration = TimeSpan.FromMilliseconds(160 * Math.Abs(to - from) / Travel),
            DecelerationRatio = 0.65
        };
        Position(slide, from, to, fromText, 2 * to - scrollBy);
        slide.Completed += (_, _) =>
        {
            if (transition != slide) return;
            CancelTransition();
            Finish();
        };
        transition = slide;
        slide.Begin(this, true);
    }

    private void Position(Storyboard slide, double from, double to, double fromText, double toText)
    {
        void Move(FrameworkElement element, double start, double end)
        {
            ((TranslateTransform)element.RenderTransform).Y = start;
            if (slide == null) return;
            var motion = new DoubleAnimation(start, end, slide.Duration);
            Storyboard.SetTarget(motion, element);
            Storyboard.SetTargetProperty(motion, SlideY);
            slide.Children.Add(motion);
        }

        Move(upper, from, to);
        // The lower clip inherits the upper clip's movement.
        Move(lower, -2 * from, -2 * to);
        Move(body, fromText, toText);
        Move(header, BarHeight * (from / Travel - 1), BarHeight * (to / Travel - 1));
        Move(footer, BarHeight * (1 - from / Travel), BarHeight * (1 - to / Travel));
    }

    private void CancelTransition()
    {
        transition?.Remove(this);
        transition = null;
    }
}
