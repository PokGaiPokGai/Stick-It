using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StickyNotesDemo;

internal sealed class NoteChrome : Grid
{
    internal const double BarHeight = 28;
    internal const double Corner = 10;
    private static readonly DependencyProperty RevealProperty = DependencyProperty.Register(
        "Reveal", typeof(double), typeof(NoteChrome),
        new PropertyMetadata(0d, (d, _) => ((NoteChrome)d).UpdateSurface()));

    private readonly RectangleGeometry outline = new() { RadiusX = Corner, RadiusY = Corner };
    private readonly TranslateTransform upper = new(), lower = new();
    private readonly FrameworkElement header, footer;
    private readonly RowDefinition topRow = new(), bottomRow = new();
    private AnimationClock transition;
    internal bool BarsVisible => header.Visibility == Visibility.Visible;
    internal double SlotHeight => topRow.Height.Value;
    internal Rect SurfaceBounds => outline.Rect;

    internal NoteChrome(Border paper, FrameworkElement header, FrameworkElement footer)
    {
        this.header = header;
        this.footer = footer;
        RowDefinitions.Add(topRow);
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(bottomRow);
        SetRow(paper, 1);
        Children.Add(paper);
        AddControls(header, upper, 0);
        AddControls(footer, lower, 2);
        SetSlotHeight(BarHeight);
    }

    private void AddControls(FrameworkElement controls, TranslateTransform shift, int row)
    {
        controls.RenderTransform = shift;
        controls.Visibility = Visibility.Hidden;
        var slot = new Grid { ClipToBounds = true };
        slot.Children.Add(controls);
        SetRow(slot, row);
        Children.Add(slot);
    }

    internal void SetSlotHeight(double height)
    {
        if (SlotHeight == height) return;
        topRow.Height = bottomRow.Height = new GridLength(height);
        UpdateSurface();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateSurface();
    }

    // Draw one continuous sheet. Its animated outline does not clip, transform
    // or invalidate the document visual. The same outline defines mouse input.
    protected override void OnRender(DrawingContext dc) => dc.DrawGeometry(Background, null, outline);
    internal bool ContainsSurface(Point point) => outline.FillContains(point);

    private void UpdateSurface()
    {
        double inset = SlotHeight * (1 - (double)GetValue(RevealProperty));
        upper.Y = inset;
        lower.Y = -inset;
        outline.Rect = new Rect(0, inset, RenderSize.Width, Math.Max(0, RenderSize.Height - 2 * inset));
    }

    internal void SetEditing(bool editing, bool animate)
    {
        var from = (double)GetValue(RevealProperty);
        double to = editing ? 1 : 0;
        header.IsHitTestVisible = footer.IsHitTestVisible = editing;
        if (editing) header.Visibility = footer.Visibility = Visibility.Visible;

        void Finish()
        {
            SetValue(RevealProperty, to);
            ApplyAnimationClock(RevealProperty, null);
            transition = null;
            if (!editing) header.Visibility = footer.Visibility = Visibility.Hidden;
        }

        if (!animate || from == to)
        {
            Finish();
            return;
        }

        var slide = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(150 * Math.Abs(to - from)))
            { DecelerationRatio = 0.5 }.CreateClock();
        slide.Completed += (_, _) =>
        {
            if (transition != slide) return;
            Finish();
        };
        transition = slide;
        ApplyAnimationClock(RevealProperty, slide);
    }
}
