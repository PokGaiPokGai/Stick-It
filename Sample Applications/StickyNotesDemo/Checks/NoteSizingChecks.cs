using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNotesDemo;

internal static partial class Checks
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(DesktopIntegration.Point point);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, out int value, int size, out int needed);

    // This dedicated process never switches the input desktop. Check UOI_IO on
    // the actual UI thread before constructing or showing any WPF window.
    private static int CheckFittedWindow()
    {
        var name = "StickIt.Height." + Guid.NewGuid().ToString("N");
        return OnNonInputDesktop(name, () =>
        {
            TestFittedWindow(name);
            Console.WriteLine("Passed on an isolated non-input Windows desktop.");
        });
    }

    private static int OnNonInputDesktop(string name, Action run)
    {
        Exception failure = null;
        var ui = new Thread(() =>
        {
            try
            {
                var desktop = CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero,
                    0, 0x0001 | 0x0002 | 0x0040 | 0x0080, IntPtr.Zero);
                if (desktop == IntPtr.Zero || !SetThreadDesktop(desktop)) throw new Win32Exception();
                if (!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 6, out var receivesInput, 4, out _))
                    throw new Win32Exception();
                if (receivesInput != 0) throw new InvalidOperationException("Refusing to test on the user's input desktop.");
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                run();
            }
            catch (Exception error) { failure = error; }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        if (failure == null) return 0;
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static void TestFittedBounds(string temp, string output)
    {
        foreach (var scale in new[] { 1.0, 1.1, 1.25, 1.5, 2.0 })
        {
            var work = new DesktopIntegration.Rect { Left = -1920, Top = -1080, Right = 0, Bottom = -40 };
            var bounds = new DesktopIntegration.Rect { Left = -900, Top = -120, Right = -500, Bottom = -45 };
            var display = new DesktopIntegration.NoteDisplay(bounds, work, scale);
            var shortNote = display.FitWindow(32.62, bounds.Top);
            var longNote = display.FitWindow(100000, bounds.Top);
            var minimum = (int)Math.Ceiling(160 * scale);
            var bar = (int)Math.Round(28 * scale);
            var minimumWithBars = minimum + 2 * bar;
            Assert(shortNote.Top == Math.Clamp(bounds.Top - bar, work.Top, work.Bottom - minimumWithBars) &&
                   shortNote.Bottom - shortNote.Top == minimumWithBars &&
                   longNote.Top == work.Top && longNote.Bottom == work.Bottom && longNote.Left == bounds.Left && longNote.Right == bounds.Right,
                $"a short note fits its 160-DIP body plus both full bars and a long note fits the work area at {scale:P0}");
            foreach (var text in new[] { minimum - 1, minimum, minimum + 1 })
            {
                var window = display.FitWindow((text - .25) / scale, -800);
                Assert(window.Bottom - window.Top == Math.Max(minimum, text) + 2 * bar && window.Top + bar == -800,
                    $"the paper starts growing above its own minimum at {scale:P0} with {text}px of content");
            }
        }
        var data = new NoteData { Markdown = "## 今日待办\n\n- [ ] 整理参考\n- [x] 保存修改", Height = 640 };
        var note = new Window2(data);
        try
        {
            var chrome = (NoteChrome)note.Content;
            var h = note.MeasurePaperHeight(200, 900);
            Render(chrome, 200, 216, Path.Combine(output, "fit-reading.png"));
            Assert(33 < h && h < 160 && chrome.SurfaceBounds == new Rect(0, 28, 200, 160),
                "short formatted notes occupy a 200 by 160 sheet between two transparent strips");
            Invoke(note, "BeginEditing");
            var editHeight = note.MeasurePaperHeight(200, 900);
            Render(chrome, 200, 216, Path.Combine(output, "fit-editing.png"));
            Assert(editHeight == h && chrome.SurfaceBounds == new Rect(0, 0, 200, 216) && Math.Abs(note.MeasurePaperHeight(200, 75) - 75) < .1,
                "editing draws a single 200 by 216 sheet with both 28-DIP bars outside the body");
            data.Height = Math.Ceiling(h);
            var store = new NoteStore(Path.Combine(temp, "fitted-notes"));
            store.Save([data]);
            var restored = store.Load().Single();
            Assert(restored.Id == data.Id && restored.Height == data.Height && restored.Markdown == data.Markdown,
                "previously saved heights below 160 still load with their original text and identity");
            data.Height = 0;
            store.Save([data]);
            try { store.Load(); throw new Exception("Invalid zero height accepted."); }
            catch (InvalidDataException) { Console.WriteLine("PASS the store still rejects invalid zero heights"); }
        }
        finally { note.DisposeWindow(); }
    }

    private static void TestEditClick(Window2 note, RichTextBox editor, Action settle)
    {
        var chrome = (NoteChrome)note.Content;
        var handle = new WindowInteropHelper(note).Handle;
        var display = DesktopIntegration.GetNoteDisplay(handle);
        var bounds = display.Bounds;
        var behind = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Left = note.Left, Top = note.Top, Width = note.ActualWidth, Height = note.ActualHeight
        };
        try
        {
            behind.Show();
            settle();
            var other = new WindowInteropHelper(behind).Handle;
            var reading = chrome.SurfaceBounds;
            var top = new DesktopIntegration.Point { X = (bounds.Left + bounds.Right) / 2, Y = bounds.Top + display.BarPixels / 2 };
            var bottom = new DesktopIntegration.Point { X = top.X, Y = bounds.Bottom - display.BarPixels / 2 - 1 };
            var middle = new DesktopIntegration.Point { X = top.X, Y = (bounds.Top + bounds.Bottom) / 2 };
            Assert(WindowFromPoint(top) == other && WindowFromPoint(bottom) == other && WindowFromPoint(middle) == handle,
                "both retracted toolbar strips pass through to the window underneath the reading note");

            var run = ((Paragraph)editor.Document.Blocks.FirstBlock).Inlines.OfType<Run>().First();
            var start = run.ContentStart.GetPositionAtOffset(1).GetCharacterRect(LogicalDirection.Forward);
            var end = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
            var origin = editor.TranslatePoint(start.TopLeft, chrome);
            var area = new Int32Rect((int)Math.Ceiling(origin.X), (int)Math.Floor(origin.Y),
                (int)Math.Floor(end.X - start.X), (int)Math.Ceiling(start.Height));
            byte[] TextPixels()
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chrome.ActualWidth), (int)Math.Ceiling(chrome.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(chrome);
                var bytes = new byte[area.Width * area.Height * 4];
                bitmap.CopyPixels(area, bytes, area.Width * 4, 0);
                return bytes;
            }
            var baseline = TextPixels();
            Assert(Enumerable.Range(0, baseline.Length / 4).Count(i => baseline[4 * i + 2] < 100) > 15,
                "the click sample contains actual text away from the blinking caret");
            int nativeChanges = 0;
            void Moved(object sender, EventArgs e) => nativeChanges++;
            void Resized(object sender, SizeChangedEventArgs e) => nativeChanges++;
            note.LocationChanged += Moved;
            note.SizeChanged += Resized;
            try
            {
                editor.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                    { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = run });
                editor.Focus();
                Assert(!editor.IsReadOnly && baseline.SequenceEqual(TextPixels()), "clicking text preserves its pixels immediately on entry");
                var slide = Slide(chrome);
                if (slide != null)
                {
                    foreach (var t in new[] { 0, .15, .5, .9, .999 })
                    {
                        Sample(chrome, slide, t);
                        if (!baseline.SequenceEqual(TextPixels())) throw new Exception($"Text flickered at click frame {t}.");
                    }
                    EndSlide(chrome, slide);
                }
                settle();
                Assert(nativeChanges == 0 && DesktopIntegration.GetNoteDisplay(handle).Bounds.Equals(bounds) && baseline.SequenceEqual(TextPixels()),
                    "a routed text click and keyboard focus retain identical glyph pixels without a native move or resize");
                var expanded = chrome.SurfaceBounds;
                Assert(Math.Abs((reading.Top - expanded.Top) * display.Scale - display.BarPixels) < .01 &&
                       Math.Abs((expanded.Bottom - reading.Bottom) * display.Scale - display.BarPixels) < .01 &&
                       WindowFromPoint(top) == handle && WindowFromPoint(bottom) == handle,
                    "each edge expands by one full toolbar height and the newly drawn strips receive input");
            }
            finally { note.SizeChanged -= Resized; note.LocationChanged -= Moved; }
        }
        finally { behind.Close(); }
    }

    private static void TestFittedWindow(string desktopName)
    {
        var settingsDirectory = Directory.CreateTempSubdirectory("StickIt-Fit-");
        var library = new MainWindow(new NoteStore(settingsDirectory.FullName));
        var font = (Slider)library.FindName("FontSizeSlider");
        var data = new NoteData { Width = 200, Height = 33, Left = 120, Top = 120, Markdown = "一行待办" };
        var note = (Window2)Invoke(library, "AddNote", data);
        try
        {
            TestPinnedWindowOrder(desktopName, Settle);
            note.Open(activate:false);
            var handle = new WindowInteropHelper(note).Handle;
            var editor = Find<RichTextBox>(note);
            var scroll = Find<ScrollViewer>(editor);
            var thumb = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
            int Pixels() => (int)Math.Round(((NoteChrome)note.Content).SurfaceBounds.Height * DesktopIntegration.GetNoteDisplay(handle).Scale);
            void Settle()
            {
                // Allow WPF's real layout, scrolling, queued fitting and toolbar
                // animation to finish on this desktop.
                var frame = new DispatcherFrame();
                var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(240) };
                timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
                timer.Start();
                Dispatcher.PushFrame(frame);
            }
            void Fits(string description)
            {
                var d = DesktopIntegration.GetNoteDisplay(handle);
                var extra = editor.ViewportHeight - editor.ExtentHeight;
                var minViewport = Math.Ceiling(160 * d.Scale) / d.Scale - 14;
                Assert(extra >= -0.02 && Math.Abs(editor.ViewportHeight - Math.Max(minViewport, editor.ExtentHeight)) < 2 &&
                       (thumb.Opacity == 0 || !thumb.IsVisible) &&
                       d.Bounds.Top >= d.Work.Top && d.Bounds.Bottom <= d.Work.Bottom,
                    $"{description} (window={Pixels()}px, extent={editor.ExtentHeight:F2}, viewport={editor.ViewportHeight:F2})");
            }
            var firstHeight = Pixels();
            Settle();
            Fits("an existing one-line note opens at its new minimum without showing a scrollbar");
            var shortHeight = Pixels();
            var screen = DesktopIntegration.GetNoteDisplay(handle);
            var minHeight = (int)Math.Ceiling(160 * screen.Scale);
            var bars = screen.BarPixels;
            Assert(shortHeight == firstHeight && shortHeight == minHeight && data.Height == minHeight / screen.Scale && note.ActualWidth == 200,
                $"the first visible frame restores a 200 by 160 reading body from a legacy height ({firstHeight}px)");
            Assert(data.Top == 120 && Math.Abs(note.Top + bars / screen.Scale - data.Top) < .6,
                "opening a saved note keeps the original reading paper anchor");
            TestEditClick(note, editor, Settle);
            Fits("editing the short note fits its full text and both bars");
            var editHeight = Pixels();
            Assert(editHeight == shortHeight + 2 * bars && note.ActualWidth == 200,
                $"the minimum editing note keeps its full 200 by 160 body and adds a complete toolbar above and below ({editHeight}px)");
            var row = (Paragraph)editor.Document.Blocks.FirstBlock;
            var run = row.Inlines.OfType<Run>().First();
            var moreLines = new Span();
            for (int i = 0; i < 5; i++)
            {
                moreLines.Inlines.Add(new LineBreak());
                moreLines.Inlines.Add(new Run("追加一行"));
            }
            row.Inlines.Add(moreLines);
            Settle();
            Fits("six lines fit in the editing window with no scrollbar");
            Assert(Pixels() == editHeight, "six lines still fit inside the minimum body with both bars fully expanded");
            font.Value = 200;
            Settle();
            var largeHeight = Pixels();
            Assert(largeHeight > editHeight && Math.Abs(largeHeight - 2 * bars - (2 * editor.ExtentHeight + 14) * screen.Scale) <= 1.01 &&
                   scroll.ScrollableHeight < .02 && thumb.Opacity == 0,
                $"200% text refits a 200-wide editing note to its last line without a scrollbar ({largeHeight}px)");
            Invoke(note, "FinishEditing");
            Settle();
            Assert(Pixels() == largeHeight - 2 * bars, "large reading text retains the full travel of both toolbars");
            font.Value = 100;
            Settle();
            Assert(Pixels() == minHeight && data.Height == minHeight / screen.Scale, "reading retracts both full bars to the 160-DIP body");
            TestEditClick(note, editor, Settle);
            row.Inlines.Remove(moreLines);
            Settle();
            Assert(Pixels() == editHeight, "removing the extra lines retains the minimum body and both editing bars");
            note.Width = 320;
            run.Text = string.Concat(Enumerable.Repeat("需要换行的待办内容 ", 35));
            Settle();
            Fits("typing grows the note to fit all of the wrapped lines");
            var wideHeight = Pixels();
            note.Width = 170;
            Settle();
            Fits("narrowing the note refits the lines at the new width");
            Assert(Math.Abs(note.ActualWidth - 200) < .01 && Pixels() > wideHeight,
                "resizing stops at the 200-DIP width and refits the wrapped lines");
            run.Text = "短";
            Settle();
            Fits("deleting text shrinks the editing window again");
            var d = DesktopIntegration.GetNoteDisplay(handle);
            var nearBottom = d.Work.Bottom - Pixels() - 3;
            note.Top += (nearBottom - d.Bounds.Top) / d.Scale;
            Settle();
            var anchor = note.Data.Top;
            run.Text = string.Concat(Enumerable.Repeat("需要换行的待办内容 ", 35));
            Settle();
            Fits("a note near the taskbar grows up far enough to show its last line");
            Assert(Math.Abs(note.Data.Top - anchor) < 1 && note.Top < anchor,
                $"automatic movement preserves the user's anchor (top={note.Top}, saved={data.Top}, anchor={anchor})");
            run.Text = new string('文', 82000);
            Settle();
            d = DesktopIntegration.GetNoteDisplay(handle);
            Assert(d.Bounds.Top == d.Work.Top && d.Bounds.Bottom == d.Work.Bottom && scroll.ScrollableHeight > 0 &&
                   thumb.Opacity == 1 && thumb.IsHitTestVisible,
                "long text reaches exactly the usable monitor height before showing its editing scrollbar");
            var longDocument = editor.Document;
            font.Value = 75;
            Settle();
            Assert(Pixels() == d.Work.Bottom - d.Work.Top && scroll.ScrollableHeight > 0 && thumb.Opacity == 1 &&
                   ReferenceEquals(editor.Document, longDocument),
                "resizing 82,000 characters preserves the document and keeps scrolling within one screen");
            font.Value = 100;
            Settle();
            editor.ScrollToEnd();
            Settle();
            Assert(scroll.VerticalOffset > 0, "the overflowing editor can scroll down its document");
            // Materialize the final line before comparing a real scrolled HWND.
            var lastLine = run.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
            double LastLineY() => editor.PointToScreen(lastLine.GetCharacterRect(LogicalDirection.Backward).Location).Y;
            _ = LastLineY();
            editor.ScrollToEnd();
            Settle();
            var endY = LastLineY();
            var endOffset = scroll.VerticalOffset;
            var endViewport = editor.ViewportHeight;
            foreach (var mode in new[] { "FinishEditing", "BeginEditing", "FinishEditing" })
            {
                Invoke(note, mode);
                DrainFormatting();
                var chrome = (NoteChrome)note.Content;
                var slide = Slide(chrome);
                if (slide != null)
                {
                    foreach (var t in new[] { 0, .5, .999 })
                    {
                        Sample(chrome, slide, t);
                        if (Math.Abs(LastLineY() - endY) > .1 || scroll.VerticalOffset != endOffset)
                            throw new Exception($"Scrolled {mode} frame {t}: glyph {endY} to {LastLineY()}, offset {endOffset} to {scroll.VerticalOffset}.");
                    }
                    EndSlide(chrome, slide);
                }
                Settle();
                Assert(Math.Abs(LastLineY() - endY) < .1 && scroll.VerticalOffset == endOffset && editor.ViewportHeight == endViewport,
                    $"a screen-full note keeps its last line at screen Y={endY:F1} and the exact same scroll offset through {mode}");
            }
            Assert(Pixels() == d.Work.Bottom - d.Work.Top - 2 * d.BarPixels && thumb.Opacity == 0 && !thumb.IsHitTestVisible,
                "a screen-height reading note retracts its two edges and hides its scrollbar");
            run.Text = "短";
            Settle();
            Fits("shortening the screen-height reading note removes the empty area");
            var fitted = DesktopIntegration.GetNoteDisplay(handle);
            Assert(Math.Abs(note.Top + fitted.BarPixels / fitted.Scale - Math.Min(anchor, (fitted.Work.Bottom - fitted.BarPixels - shortHeight) / fitted.Scale)) < 1 && Pixels() == shortHeight,
                "after shrinking, the short note returns to its chosen position");

            // Measure the native rectangle and the glyph from before the first frame
            // through completion at each screen edge.
            d = DesktopIntegration.GetNoteDisplay(handle);
            note.Top += (d.Work.Bottom - editHeight - 2 - d.Bounds.Top) / d.Scale;
            Settle();
            var p = run.ContentStart;
            double TextOnScreen() => editor.PointToScreen(p.GetCharacterRect(LogicalDirection.Forward).Location).Y;
            if (SystemParameters.ClientAreaAnimation)
            {
                var chrome = (NoteChrome)note.Content;
                foreach (var position in new[] { "bottom", "top", "middle" })
                {
                    d = DesktopIntegration.GetNoteDisplay(handle);
                    var wanted = position == "top" ? d.Work.Top : position == "bottom" ? d.Work.Bottom - editHeight : (d.Work.Top + d.Work.Bottom) / 2;
                    note.Top += (wanted - d.Bounds.Top) / d.Scale;
                    Settle();
                    var reading = DesktopIntegration.GetNoteDisplay(handle).Bounds;
                    var readY = TextOnScreen();
                    foreach (var mode in new[] { "BeginEditing", "FinishEditing" })
                    {
                        Invoke(note, mode);
                        DrainFormatting();
                        var slide = Slide(chrome);
                        foreach (var time in new[] { 0, .25, .5, .8, .995 })
                        {
                            Sample(chrome, slide, time);
                            if (Math.Abs(TextOnScreen() - readY) > .1) throw new Exception($"{position} {mode} frame {time}: glyph {readY} to {TextOnScreen()}");
                        }
                        EndSlide(chrome, slide);
                        var b = DesktopIntegration.GetNoteDisplay(handle).Bounds;
                        Assert(Math.Abs(TextOnScreen() - readY) < .1 && b.Equals(reading) &&
                               b.Top >= d.Work.Top && b.Bottom <= d.Work.Bottom,
                            $"{position} {mode}: the native window and the glyph stay fixed through the final frame");
                    }
                }
                Invoke(note, "BeginEditing");
                DrainFormatting();
                Sample(chrome, Slide(chrome), .6);
                Invoke(note, "FinishEditing");
                DrainFormatting();
                Sample(chrome, Slide(chrome), .25);
            }
            note.Close();
            note.Open(activate:false);
            Assert(Pixels() == shortHeight && !((NoteChrome)note.Content).BarsVisible,
                "hiding during a partially reversed slide reopens directly as a compact reading note");
            Settle();
            Fits("a previously hidden note reopens at the height of its current content");

            Invoke(note, "BeginEditing");
            Settle();
            void PasteLines(int count)
            {
                editor.SelectAll();
                var markdown = string.Join("\n", Enumerable.Repeat("**测量行**", count));
                var clip = new DataObject(DataFormats.UnicodeText, markdown);
                var paste = new DataObjectPastingEventArgs(clip, false, DataFormats.UnicodeText);
                editor.RaiseEvent(paste);
                if (!paste.CommandCancelled) throw new Exception("The formatted paste did not reach the editor.");
                Settle();
            }
            var lineHeight = run.ContentStart.GetCharacterRect(LogicalDirection.Forward).Height;
            var rows = (int)Math.Floor((d.AvailablePaperHeight - 14) / lineHeight);
            PasteLines(rows);
            Fits("a formatted Markdown paste just under one screen remains fully visible");
            Assert(d.Work.Bottom - d.Work.Top - Pixels() < 2 * lineHeight * d.Scale,
                "the fitting case exercises the boundary within two text lines of a full screen");
            PasteLines(rows + 2);
            Assert(Pixels() == d.Work.Bottom - d.Work.Top && thumb.Opacity == 1 && scroll.ScrollableHeight > 0,
                "two more pasted lines cross the screen limit and enable scrolling");
            editor.Undo();
            Settle();
            Fits("undoing the overflowing paste restores the fitted height and hides the scrollbar");

            var fresh = new Window2(new NoteData());
            try
            {
                var center = new DesktopIntegration.Point { X = (d.Work.Left + d.Work.Right) / 2, Y = (d.Work.Top + d.Work.Bottom) / 2 };
                Invoke(fresh, "BeginEditing");
                DesktopIntegration.PlaceAtPoint(fresh, center);
                fresh.Open(edit:true, activate:false);
                Settle();
                var positioned = DesktopIntegration.GetNoteDisplay(new WindowInteropHelper(fresh).Handle);
                var b = positioned.Bounds;
                Assert(Math.Abs(b.Top + b.Bottom - 2 * center.Y) <= 1 && Math.Abs(b.Left + b.Right - 2 * center.X) <= 1 &&
                       b.Bottom - b.Top == (int)Math.Ceiling(160 * positioned.Scale) + 2 * positioned.BarPixels,
                    $"a new empty editing note centers its body and both external bars on the requested point (height={b.Bottom - b.Top}px)");
            }
            finally { fresh.DisposeWindow(); }
        }
        finally
        {
            library.PrepareForShutdown();
            note.DisposeWindow();
            library.Close();
            settingsDirectory.Delete(true);
        }
    }
}
