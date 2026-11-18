using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    private static Storyboard Slide(NoteChrome chrome) => (Storyboard)typeof(NoteChrome)
        .GetField("transition", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(chrome);

    private static void Sample(NoteChrome chrome, Storyboard slide, double fraction)
    {
        slide.SeekAlignedToLastTick(chrome, slide.Duration.TimeSpan * fraction, TimeSeekOrigin.BeginTime);
        chrome.UpdateLayout();
    }

    private static void EndSlide(NoteChrome chrome, Storyboard slide)
    {
        slide.SkipToFill(chrome);
        // Process the completion callback, including cleanup and the native scroll
        // viewport change, just as the application dispatcher would.
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
            var paper = (Border)note.Content;
            var chrome = (NoteChrome)paper.Child;
            var editor = Find<RichTextBox>(chrome);
            var body = (Grid)VisualTreeHelper.GetParent(editor);
            var footer = FormattingScroller(note);
            var header = (Grid)chrome.Children[1];
            body.Children.Remove(editor);
            var probe = new LayoutProbe { Child = editor };
            body.Children.Insert(0, probe);
            void SizePaper(double w, double h)
            {
                paper.Measure(new Size(w, h));
                paper.Arrange(new Rect(0, 0, w, h));
                paper.UpdateLayout();
            }
            SizePaper(320, 190);
            var document = editor.Document;
            int changes = 0;
            editor.TextChanged += (_, _) => changes++;
            var first = document.Blocks.FirstBlock.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            double Y(FrameworkElement e) => e.TranslatePoint(new Point(), paper).Y;
            double GlyphY(TextPointer p) => editor.TranslatePoint(p.GetCharacterRect(LogicalDirection.Forward).Location, paper).Y;
            var firstY = GlyphY(first);
            var readHeight = editor.ViewportHeight;
            var readWidth = editor.ViewportWidth;

            chrome.SetEditing(true, true);
            var opening = Slide(chrome);
            Sample(chrome, opening, 0);
            var before = (probe.Measures, probe.Arranges);
            double previousText = firstY, previousTop = Y(header), previousBottom = Y(footer);
            var elapsed = Stopwatch.StartNew();
            for (int i = 1; i <= 8; i++)
            {
                Sample(chrome, opening, i / 10.0);
                var glyph = GlyphY(first);
                if (glyph <= previousText || Y(header) <= previousTop || Y(footer) >= previousBottom)
                    throw new Exception($"Opening frame {i}: text {previousText:F2} to {glyph:F2}, header {previousTop:F2} to {Y(header):F2}, footer {previousBottom:F2} to {Y(footer):F2}.");
                (previousText, previousTop, previousBottom) = (glyph, Y(header), Y(footer));
            }
            Console.WriteLine($"Long-note toolbar slide: {markdown.Length:N0} characters, 8 sampled frames in {elapsed.Elapsed.TotalMilliseconds:F1} ms; editor measures={probe.Measures-before.Measures}, arranges={probe.Arranges-before.Arranges}.");
            Assert((probe.Measures, probe.Arranges) == before && editor.ViewportHeight == readHeight,
                "animating a long note moves real glyphs and both bars without measuring or arranging the editor per frame");

            chrome.SetEditing(false, true);
            var closing = Slide(chrome);
            Sample(chrome, closing, 0);
            Assert(Math.Abs(GlyphY(first) - previousText) < 0.1 && Math.Abs(Y(header) - previousTop) < 0.1 &&
                   Math.Abs(Y(footer) - previousBottom) < 0.1 && !footer.IsHitTestVisible,
                "reversing mid-slide continues from the displayed positions and releases toolbar mouse input");
            Sample(chrome, closing, 0.5);
            Assert(GlyphY(first) < previousText && GlyphY(first) > firstY && Y(header) < previousTop && Y(footer) > previousBottom,
                "closing moves text upward and both toolbars outward before hiding them");
            var middleY = GlyphY(first);
            var footerInset = paper.ActualHeight - Y(footer);
            SizePaper(360, 230);
            Assert(Math.Abs(GlyphY(first) - middleY) < 0.1 && Math.Abs(paper.ActualHeight - Y(footer) - footerInset) < 0.1,
                "resizing during a slide keeps the same text position and footer distance from the paper edge");
            EndSlide(chrome, closing);
            Assert(footer.Visibility == Visibility.Collapsed && Math.Abs(GlyphY(first) - firstY) < 0.1,
                "the finished reading state reclaims both strips without a stale opening callback");

            SizePaper(320, 190);
            var last = document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)
                .GetNextInsertionPosition(LogicalDirection.Backward);
            // WPF formats long documents incrementally. Materialize the final
            // line before testing a viewport that has actually reached it.
            _ = GlyphY(last);
            paper.UpdateLayout();
            editor.ScrollToEnd();
            paper.UpdateLayout();
            var readLastY = GlyphY(last);
            Assert(readLastY > 0 && readLastY < paper.ActualHeight, "the stress note is scrolled to its actual final line before testing bottom anchoring");
            chrome.SetEditing(true, true);
            opening = Slide(chrome);
            Sample(chrome, opening, 0.995);
            var beforeCommit = GlyphY(last);
            EndSlide(chrome, opening);
            var editLastY = GlyphY(last);
            Assert(Math.Abs(editLastY - beforeCommit) < 1 && Math.Abs(readHeight - editor.ViewportHeight - 44) < 0.1 &&
                   editor.ViewportWidth == readWidth && Math.Abs(editor.ExtentHeight - editor.ViewportHeight - editor.VerticalOffset) < 1,
                $"the last line glides to the editing footer and stays reachable (last={beforeCommit:F1}/{editLastY:F1}, height={editor.ViewportHeight}, extent={editor.ExtentHeight}, scroll={editor.VerticalOffset}, width={editor.ViewportWidth})");
            chrome.SetEditing(false, true);
            closing = Slide(chrome);
            Sample(chrome, closing, 0);
            Assert(Math.Abs(GlyphY(last) - editLastY) < 1, "expanding a bottom-scrolled document starts from its displayed last line");
            Sample(chrome, closing, 0.5);
            var halfY = GlyphY(last);
            EndSlide(chrome, closing);
            Assert(halfY > editLastY && halfY < readLastY && Math.Abs(GlyphY(last) - readLastY) < 1,
                $"at the end of a long note the last line follows the retreating footer (edit={editLastY:F1}, mid={halfY:F1}, read={readLastY:F1})");
            Assert(ReferenceEquals(editor.Document, document) && changes == 0 && note.Data.Markdown == markdown && !note.IsVisible,
                "toolbar transitions keep the original long document, saved text and desktop untouched");
        }
        finally { note.DisposeWindow(); }

        TestNoteAtOverflowThreshold();
    }

    private static void TestNoteAtOverflowThreshold()
    {
        var note = new Window2(new NoteData { Markdown = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"第 {i} 行")) });
        try
        {
            var paper = (Border)note.Content;
            var chrome = (NoteChrome)paper.Child;
            var editor = Find<RichTextBox>(chrome);
            paper.Measure(new Size(320, 190));
            paper.Arrange(new Rect(0, 0, 320, 190));
            paper.UpdateLayout();
            var scroller = Find<ScrollViewer>(editor);
            var width = editor.ViewportWidth;
            var last = editor.Document.Blocks.LastBlock.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            Rect LastLine() => last.GetCharacterRect(LogicalDirection.Backward);
            var hitPoint = LastLine().Location + new Vector(-1, 3);
            var hit = editor.GetPositionFromPoint(hitPoint, false);
            Assert(scroller.ScrollableHeight == 0 && hit != null, "the last line is clickable in a reading note that just fits");
            editor.IsReadOnly = false;
            chrome.SetEditing(true, true, hit.GetCharacterRect(LogicalDirection.Forward).Bottom);
            editor.CaretPosition = hit;
            var slide = Slide(chrome);
            Sample(chrome, slide, 0);
            Assert(Math.Abs(LastLine().Left - hitPoint.X - 1) < 0.1, "the opening frame preserves the clicked text position");
            Sample(chrome, slide, 0.995);
            double LineBottom() => editor.TranslatePoint(LastLine().BottomLeft, paper).Y;
            var beforeCommit = LineBottom();
            EndSlide(chrome, slide);
            var footer = FormattingScroller(note);
            Assert(scroller.ScrollableHeight > 0 && editor.ViewportWidth == width &&
                   Math.Abs(LineBottom() - beforeCommit) < 1 && LineBottom() <= footer.TranslatePoint(new Point(), paper).Y + 1,
                $"a note that only overflows while editing keeps its wrap and clicked line above the toolbar (before={beforeCommit:F1}, after={LineBottom():F1})");
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
