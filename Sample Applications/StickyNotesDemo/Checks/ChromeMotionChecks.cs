using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private static AnimationClock Slide(NoteChrome chrome) => (AnimationClock)typeof(NoteChrome)
        .GetField("transition", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chrome);

    private static void Sample(NoteChrome chrome, AnimationClock slide, double fraction)
    {
        slide.Controller.SeekAlignedToLastTick(slide.Timeline.Duration.TimeSpan * fraction, TimeSeekOrigin.BeginTime);
        chrome.UpdateLayout();
    }

    private static void EndSlide(NoteChrome chrome, AnimationClock slide)
    {
        slide.Controller.SkipToFill();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        var start = Stopwatch.StartNew();
        timer.Tick += (_, _) => { if (Slide(chrome) == null || start.ElapsedMilliseconds > 1500) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (Slide(chrome) != null) throw new Exception("The toolbar transition did not finish.");
        chrome.UpdateLayout();
    }

    private static void TestChromeMotion()
    {
        var markdown = string.Join("\n\n", Enumerable.Range(1, 160).Select(i =>
            $"### 小节 {i}\n\n- [ ] **待办** {new string('文', 480)}\n- [x] 完成"));
        var note = new Window2(new NoteData { Markdown = markdown });
        try
        {
            var chrome = (NoteChrome)note.Content;
            var editor = Find<RichTextBox>(chrome);
            var body = (Grid)VisualTreeHelper.GetParent(editor);
            body.Children.Remove(editor);
            var probe = new LayoutProbe { Child = editor };
            body.Children.Insert(0, probe);
            var footer = FormattingScroller(note);
            var header = Find<Button>(chrome).Parent as Grid;
            var doc = editor.Document;
            int changes = 0;
            editor.TextChanged += (_, _) => changes++;

            const int width = 200, paperHeight = 160;
            var size = new Size(width, paperHeight + 2 * chrome.SlotHeight);
            chrome.Measure(size);
            chrome.Arrange(new Rect(size));
            chrome.UpdateLayout();
            double Y(FrameworkElement e) => e.TranslatePoint(new Point(), chrome).Y - chrome.SlotHeight;
            double GlyphY(TextPointer p) => editor.TranslatePoint(p.GetCharacterRect(LogicalDirection.Forward).Location, body).Y + Y(body);
            var first = doc.Blocks.FirstBlock.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            var firstY = GlyphY(first);
            var viewport = new Size(editor.ViewportWidth, editor.ViewportHeight);

            // Compare every alpha value with a single rounded rectangle. Checking
            // just the top and bottom pixels misses a gap or a notch at either seam.
            byte[] bodyPixels = null;
            (int Top, int Bottom) PaintedEdges()
            {
                var h = (int)chrome.ActualHeight;
                var picture = new RenderTargetBitmap(width, h, 96, 96, PixelFormats.Pbgra32);
                picture.Render(chrome);
                int stride = width * 4, top = (int)chrome.SlotHeight;
                var pixels = new byte[stride * h];
                picture.CopyPixels(pixels, stride, 0);
                var shape = new DrawingVisual();
                using (var dc = shape.RenderOpen()) dc.DrawRoundedRectangle(chrome.Background, null, chrome.SurfaceBounds, NoteChrome.Corner, NoteChrome.Corner);
                var expected = new RenderTargetBitmap(width, h, 96, 96, PixelFormats.Pbgra32);
                expected.Render(shape);
                var mask = new byte[pixels.Length];
                expected.CopyPixels(mask, stride, 0);
                for (int i = 3; i < mask.Length; i += 4)
                    if (Math.Abs(mask[i] - pixels[i]) > 1)
                        throw new Exception($"Broken paper silhouette at ({i / 4 % width}, {i / stride}): alpha {pixels[i]} vs {mask[i]}.");
                foreach (var x in new[] { 4, width - 5 })
                    for (int y = 0; y < h; y++)
                    {
                        int i = y * stride + x * 4;
                        if (mask[i + 3] == 255 && !pixels.AsSpan(i, 4).SequenceEqual(mask.AsSpan(i, 4)))
                            throw new Exception($"The sheet changed color at ({x}, {y}).");
                    }
                var text = new byte[(width - 36) * 4 * (paperHeight - 14)];
                picture.CopyPixels(new Int32Rect(17, top + 7, width - 36, paperHeight - 14), text, (width - 36) * 4, 0);
                bodyPixels ??= text;
                if (!text.SequenceEqual(bodyPixels)) throw new Exception("A text pixel changed during the toolbar transition.");
                var opaque = Enumerable.Range(0, h).Where(y => pixels[y * stride + width / 2 * 4 + 3] > 127);
                return (opaque.First() - top, opaque.Last() - top);
            }

            var closedEdges = PaintedEdges();
            Assert(closedEdges == (0, paperHeight - 1), "only the 200 by 160 paper is painted before editing");
            chrome.SetEditing(true, true);
            var opening = Slide(chrome);
            Sample(chrome, opening, 0);
            var before = (probe.Measures, probe.Arranges);
            var initial = PaintedEdges();
            Assert(initial == closedEdges && Math.Abs(GlyphY(first) - firstY) < .01, "the opening frame paints only the original paper at the original coordinates");
            double lastTop = Y(header), lastBottom = Y(footer);
            var lastEdges = initial;
            for (int i = 1; i <= 8; i++)
            {
                Sample(chrome, opening, i / 10.0);
                var edges = PaintedEdges();
                if (Y(header) >= lastTop || Y(footer) <= lastBottom || edges.Top >= lastEdges.Top || edges.Bottom <= lastEdges.Bottom ||
                    Math.Abs(GlyphY(first) - firstY) >= .01)
                    throw new Exception($"Outward frame {i}: edges {lastEdges} to {edges}, glyph {firstY:F2} to {GlyphY(first):F2}.");
                (lastTop, lastBottom, lastEdges) = (Y(header), Y(footer), edges);
            }
            Assert((probe.Measures, probe.Arranges) == before && editor.ViewportHeight == viewport.Height,
                $"one gapless rounded sheet expands over eight frames with identical text pixels and zero editor layout passes on {markdown.Length:N0} characters");

            chrome.SetEditing(false, true);
            var closing = Slide(chrome);
            Sample(chrome, closing, 0);
            Assert(PaintedEdges() == lastEdges && Math.Abs(Y(header) - lastTop) < .01 && Math.Abs(Y(footer) - lastBottom) < .01 &&
                   !footer.IsHitTestVisible, "reversing the slide preserves both displayed edges and releases the buttons");
            Sample(chrome, closing, .5);
            var middle = PaintedEdges();
            Assert(middle.Top > lastEdges.Top && middle.Bottom < lastEdges.Bottom && Math.Abs(GlyphY(first) - firstY) < .01,
                "closing retracts the upper strip down and the lower strip up while the text stays still");
            Sample(chrome, closing, .999);
            var lastFrame = PaintedEdges();
            EndSlide(chrome, closing);
            Assert(lastFrame == closedEdges && PaintedEdges() == closedEdges && Math.Abs(GlyphY(first) - firstY) < .01 &&
                   !chrome.BarsVisible, "the final animation frame and the compact window paint the same edges and glyph");
            chrome.SetEditing(true, false);
            var openEdges = PaintedEdges();
            Assert(closedEdges.Top - openEdges.Top == 28 && openEdges.Bottom - closedEdges.Bottom == 28,
                "the painted edges of a minimum note travel exactly 28 DIPs above and below the paper");
            chrome.SetEditing(false, false);
            Assert(PaintedEdges() == closedEdges, "the full editing and compact reading silhouettes are also continuous with animations off");

            var last = doc.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            _ = GlyphY(last);
            chrome.UpdateLayout();
            editor.ScrollToEnd();
            chrome.UpdateLayout();
            var lastY = GlyphY(last);
            var offset = editor.VerticalOffset;
            Assert(offset > 0 && lastY >= 0 && lastY < paperHeight, "the long document is scrolled to its actual last line");
            foreach (var editing in new[] { true, false })
            {
                chrome.SetEditing(editing, true);
                var slide = Slide(chrome);
                foreach (var t in new[] { 0, .3, .6, .995 })
                {
                    Sample(chrome, slide, t);
                    if (Math.Abs(GlyphY(last) - lastY) >= .01 || editor.VerticalOffset != offset) throw new Exception("The last line or scroll offset moved.");
                }
                EndSlide(chrome, slide);
                Assert(Math.Abs(GlyphY(last) - lastY) < .01 && editor.VerticalOffset == offset &&
                       editor.ViewportHeight == viewport.Height && editor.ViewportWidth == viewport.Width,
                    $"the last line, scroll offset and available text area stay fixed with editing={editing}");
            }
            Assert(ReferenceEquals(editor.Document, doc) && changes == 0 && note.Data.Markdown == markdown && !note.IsVisible,
                "toolbar transitions preserve the same formatted document and saved text");
        }
        finally { note.DisposeWindow(); }
    }

    private sealed class LayoutProbe : Decorator
    {
        internal int Measures, Arranges;
        protected override Size MeasureOverride(Size available) { Measures++; return base.MeasureOverride(available); }
        protected override Size ArrangeOverride(Size size) { Arranges++; return base.ArrangeOverride(size); }
    }
}
