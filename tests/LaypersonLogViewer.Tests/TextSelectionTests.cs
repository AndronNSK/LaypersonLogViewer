using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class TextSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DragCopiesOneRangeAcrossGroupedEntries(bool wordWrap, bool backwards)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.WordWrap = wordWrap;
            var continuation = "  " + new string('x', 190);
            await Load(window, $"12:00:00 first entry\n{continuation}\n12:00:01 next entry\n  last line");
            await model.SetTimestampPatternAsync(new TimestampPattern("12:00:00", 0));
            window.UpdateLayout();
            Assert.Equal(2, model.EntryCount);
            var texts = RealizedTexts(window);
            var from = AtCharacter(window, texts[0], 9);
            var to = AtCharacter(window, texts[3], 6);
            window.MouseMove(default);
            window.MouseDown(backwards ? to : from, MouseButton.Left);
            window.MouseMove(backwards ? from : to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(backwards ? from : to, MouseButton.Left);

            Assert.Equal("first entry", texts[0].SelectedText);
            Assert.Equal(continuation, texts[1].SelectedText);
            Assert.Equal("12:00:01 next entry", texts[2].SelectedText);
            Assert.Equal("  last", texts[3].SelectedText);
            var expected = string.Join(Environment.NewLine, "first entry", continuation, "12:00:01 next entry", "  last");
            if (backwards)
            {
                var inside = AtCharacter(window, texts[1], 5);
                window.MouseMove(inside);
                window.MouseDown(inside, MouseButton.Right);
                window.MouseUp(inside, MouseButton.Right);
                var menu = texts[1].ContextMenu!;
                var copy = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Копировать"));
                Assert.True(copy.IsEnabled);
                copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else PressCopy(window);
            Assert.Equal(expected, await window.Clipboard!.TryGetTextAsync());

            var click = AtCharacter(window, texts[2], 16);
            window.MouseMove(click);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Assert.All(texts, text => Assert.Empty(text.SelectedText));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DragPastViewportScrollsWhileSelectingText()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, string.Join('\n', Enumerable.Range(1, 200).Select(number => $"row {number}")));
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(viewer => ReferenceEquals(viewer.TemplatedParent, list));
            var texts = RealizedTexts(window);
            var from = AtCharacter(window, texts[0], 0);
            var next = AtCharacter(window, texts[1], 4);
            var pastBottom = new Point(next.X, list.TranslatePoint(default, window)!.Value.Y + list.Bounds.Height + 2);
            window.MouseMove(default);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(next, RawInputModifiers.LeftMouseButton);
            window.MouseMove(pastBottom, RawInputModifiers.LeftMouseButton);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (scroll.Offset.Y < 64) await Task.Delay(10, timeout.Token);
            window.MouseUp(pastBottom, MouseButton.Left);

            Assert.True(scroll.Offset.Y >= 64);
            Assert.False(((MainWindowViewModel)window.DataContext!).Live.FollowLatest);
            PressCopy(window);
            var copied = await window.Clipboard!.TryGetTextAsync();
            Assert.StartsWith("row 1" + Environment.NewLine + "row 2" + Environment.NewLine, copied);
            Assert.True(copied!.Split(Environment.NewLine).Length > 10);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MultilineCopySurvivesStreamingAndVirtualizationAndSkipsHiddenLines()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            var lines = Enumerable.Range(1, 200).Select(number => $"row {number}").ToArray();
            lines[1] = "hidden";
            await Load(window, string.Join('\n', lines));
            await model.AddFilterAsync(new LogFilter(FilterKind.Exclude, "hidden"));
            window.UpdateLayout();
            var texts = RealizedTexts(window);
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(viewer => ReferenceEquals(viewer.TemplatedParent, list));
            var from = AtCharacter(window, texts[0], 4);
            var next = AtCharacter(window, texts[1], 3);
            window.MouseMove(default);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(next, RawInputModifiers.LeftMouseButton);
            await model.ApplyLiveLinesAsync("stream", [..model.AllLines, new(201, "new line")]);
            scroll.Offset = new Vector(0, scroll.ScrollBarMaximum.Y);
            window.UpdateLayout();
            var bottomTexts = RealizedTexts(window);
            Assert.DoesNotContain(bottomTexts, text => ((LogLineRow)text.DataContext!).Number == 1);
            var to = AtCharacter(window, bottomTexts.Last(text => text.Text == "row 200"), 3);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);

            PressCopy(window);
            var expected = string.Join(Environment.NewLine, lines.Skip(2).SkipLast(1).Prepend("1").Append("row"));
            Assert.Equal(expected, await window.Clipboard!.TryGetTextAsync());
            Assert.Equal("row", RealizedTexts(window).Single(text => text.Text == "row 200").SelectedText);
            scroll.Offset = default;
            window.UpdateLayout();
            Assert.Equal("1", RealizedTexts(window)[0].SelectedText);
        }
        finally { window.Close(); }
    }

    private static TextBox[] RealizedTexts(MainWindow window) =>
        window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>()
            .Where(text => text.DataContext is LogLineRow)
            .OrderBy(text => ((LogLineRow)text.DataContext!).Number).ToArray();

    private static Point AtCharacter(MainWindow window, TextBox text, int index)
    {
        var presenter = text.GetVisualDescendants().OfType<TextPresenter>().Single();
        var caret = presenter.TextLayout.HitTestTextPosition(index);
        return presenter.TranslatePoint(new Point(caret.X + 1, caret.Center.Y), window)!.Value;
    }

    private static void PressCopy(MainWindow window)
    {
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectingAnotherLogLineClearsThePreviousHighlight(bool wordWrap)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.WordWrap = wordWrap;
            await Load(window, "first row has selectable text\nsecond row has selectable text\nthird row has selectable text");
            var list = window.FindControl<ListBox>("LogLines")!;
            var texts = list.GetVisualDescendants().OfType<TextBox>()
                .OrderBy(text => ((LogLineRow)text.DataContext!).Number).ToArray();
            window.MouseMove(default);

            void DragAcross(TextBox text)
            {
                var start = text.TranslatePoint(new Point(6, 5), window)!.Value;
                var end = start + new Vector(95, 0);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                window.MouseUp(end, MouseButton.Left);
            }

            DragAcross(texts[0]);
            var firstSelection = texts[0].SelectedText;
            Assert.NotEmpty(firstSelection);
            Assert.True(window.FindControl<Button>("SearchButton")!.Focus());
            Assert.Equal(firstSelection, texts[0].SelectedText);

            DragAcross(texts[1]);
            Assert.NotEmpty(texts[1].SelectedText);
            Assert.Empty(texts[0].SelectedText);

            var third = texts[2].TranslatePoint(new Point(6, 5), window)!.Value;
            window.MouseDown(third, MouseButton.Left);
            window.MouseUp(third, MouseButton.Left);
            Assert.All(texts, text => Assert.Empty(text.SelectedText));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task WordWrapFitsLongLinesAndPreservesSelectionDuringStreaming()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var content = string.Join(" ", Enumerable.Repeat("long log message", 40));
            await Load(window, content);
            var model = (MainWindowViewModel)window.DataContext!;
            var list = window.FindControl<ListBox>("LogLines")!;
            var box = window.FindControl<CheckBox>("WordWrapBox")!;
            var text = list.GetVisualDescendants().OfType<TextBox>().Single();
            var originalHeight = text.Bounds.Height;
            text.SelectionStart = 5;
            text.SelectionEnd = 20;
            var selected = text.SelectedText;
            Assert.False(model.WordWrap);
            box.IsChecked = true;
            window.UpdateLayout();
            Assert.True(model.WordWrap);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
            Assert.True(text.Bounds.Height > originalHeight);
            Assert.True(text.Bounds.Width < list.Bounds.Width);
            Assert.Equal(selected, text.SelectedText);
            await model.ApplyLiveLinesAsync("stream", [model.AllLines[0], new(2, content)]);
            window.UpdateLayout();
            Assert.Same(text, list.GetVisualDescendants().OfType<TextBox>().First());
            Assert.All(list.GetVisualDescendants().OfType<TextBox>(), row =>
                Assert.Equal(Avalonia.Media.TextWrapping.Wrap, row.TextWrapping));
            Assert.Equal(selected, text.SelectedText);
            box.IsChecked = false;
            window.UpdateLayout();
            Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, text.TextWrapping);
            Assert.Equal(originalHeight, text.Bounds.Height);
            Assert.Equal(content, text.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, FilterKind.Include)]
    [InlineData(false, FilterKind.Exclude)]
    public async Task FilterUsesCurrentSelectionAndAcceptsTypedEdits(bool contextMenu, FilterKind kind)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            const string selected = " Ошибка.* ";
            await Load(window, "old selection\nprefix" + selected + "suffix");
            var texts = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants()
                .OfType<TextBox>().ToArray();
            texts[0].Focus();
            texts[0].SelectAll();
            texts[1].Focus();
            texts[1].SelectionStart = 6;
            texts[1].SelectionEnd = 6 + selected.Length;
            if (contextMenu)
            {
                var menu = OpenMenu(window, texts[1]);
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, kind == FilterKind.Include ? "Показать строки" : "Скрыть строки"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                window.MouseMove(new Point(0, 0));
                var button = window.FindControl<Button>(kind == FilterKind.Include ? "IncludeButton" : "ExcludeButton")!;
                var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }

            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            var input = dialog.FindControl<TextBox>("PatternBox")!;
            Assert.Equal(selected, input.Text);
            Assert.False(input.IsReadOnly);
            // Type through the input system, rather than assigning the Text property.
            dialog.KeyTextInput("edited filter");
            Assert.Equal("edited filter", input.Text);
            dialog.FindControl<Button>("AddButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var model = (MainWindowViewModel)window.DataContext!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (model.Filters.Count == 0 || model.IsBusy) await Task.Delay(10, timeout.Token);
            Assert.Equal("edited filter", Assert.Single(model.Filters).Text);
            Assert.Equal(kind, model.Filters[0].Kind);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, true)]
    public async Task FilterDialogPreservesSelectionOnlyOnSurvivingLine(
        bool contextMenu, bool exclude, bool greyMode, bool cancel)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "before\nERROR failed\nafter");
            var model = (MainWindowViewModel)window.DataContext!;
            model.ShowFilteredOut = greyMode;
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants()
                .OfType<TextBox>().Single(t => t.Text == "ERROR failed");
            text.Focus();
            text.SelectionStart = 5;
            text.SelectionEnd = 0; // Also preserve a selection dragged backwards.
            if (contextMenu)
            {
                var menu = OpenMenu(window, text);
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, exclude ? "Скрыть строки" : "Показать строки"))
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            else
            {
                window.MouseMove(new Point(0, 0));
                var button = window.FindControl<Button>(exclude ? "ExcludeButton" : "IncludeButton")!;
                var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            if (contextMenu) Assert.Empty(window.OwnedWindows);
            else
            {
                var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
                // A toolbar exclusion removes another row; the context exclusion removes this row.
                dialog.FindControl<TextBox>("PatternBox")!.Text = exclude ? "before" : "ERROR";
                dialog.FindControl<Button>(cancel ? "CancelButton" : "AddButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if (!cancel)
                while (model.Filters.Count == 0 || model.IsBusy) await Task.Delay(10, timeout.Token);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            var texts = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants()
                .OfType<TextBox>().ToArray();
            if (contextMenu && exclude && !greyMode && !cancel)
            {
                Assert.DoesNotContain(texts, t => t.Text == "ERROR failed");
                Assert.All(texts, t => Assert.Empty(t.SelectedText));
            }
            else
            {
                var retained = Assert.Single(texts, t => t.Text == "ERROR failed");
                Assert.Equal("ERROR", retained.SelectedText);
                Assert.Equal(5, retained.SelectionStart);
                Assert.Equal(0, retained.SelectionEnd);
                Assert.All(texts.Where(t => t != retained), t => Assert.Empty(t.SelectedText));
            }
        }
        finally { window.Close(); }
    }

    private static async Task Load(MainWindow window, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await ((MainWindowViewModel)window.DataContext!).LoadAsync("test.log", stream);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static TextBox FirstText(MainWindow window) =>
        window.FindControl<ListBox>("LogLines")!.GetVisualDescendants()
            .OfType<TextBox>().First();

    private static ContextMenu OpenMenu(MainWindow window, TextBox text)
    {
        // Use an actual right click to verify that it preserves the selection.
        window.MouseMove(new Point(0, 0)); // Settle rendering before calculating screen positions.
        var point = text.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(text.ContextMenu is not null, $"Text bounds: {text.Bounds}; point: {point}; hit: {window.InputHitTest(point)?.GetType().Name}; selection: {text.SelectedText}");
        Assert.True(text.ContextMenu.IsOpen);
        return text.ContextMenu;
    }

    [AvaloniaTheory]
    [InlineData(FilterKind.Include, false)]
    [InlineData(FilterKind.Exclude, false)]
    [InlineData(FilterKind.Include, true)]
    [InlineData(FilterKind.Exclude, true)]
    public async Task ContextMenuAddsExactSelectedTextWithoutDialog(FilterKind kind, bool greyMode)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            const string selected = " Ошибка.* ";
            await Load(window, "prefix" + selected + "suffix\nINFO ready");
            var model = (MainWindowViewModel)window.DataContext!;
            if (greyMode)
            {
                model.ShowFilteredOut = true;
                await model.AddFilterAsync(new LogFilter(FilterKind.Include, "INFO"));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
            var countBefore = model.Filters.Count;
            var text = FirstText(window);
            text.Focus();
            text.SelectionStart = 6;
            text.SelectionEnd = 6 + selected.Length;
            Assert.Equal(selected, text.SelectedText);
            var menu = OpenMenu(window, text);
            var item = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, kind == FilterKind.Include ? "Показать строки" : "Скрыть строки"));
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Empty(window.OwnedWindows);
            Assert.False(menu.IsOpen);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (model.Filters.Count == countBefore || model.IsBusy)
                await Task.Delay(10, timeout.Token);
            var filter = model.Filters.Last();
            Assert.Equal(selected, filter.Text);
            Assert.Equal(kind, filter.Kind);
            Assert.False(filter.CaseSensitive);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    public async Task EmptyOrWhitespaceSelectionDisablesFilterActions(int start, int end)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "   ERROR");
            var text = FirstText(window);
            text.Focus();
            text.SelectionStart = start;
            text.SelectionEnd = end;
            var menu = OpenMenu(window, text);
            Assert.All(menu.Items.OfType<MenuItem>().Take(3), item => Assert.False(item.IsEnabled));
            menu.Close();
            Assert.Empty(((MainWindowViewModel)window.DataContext!).Filters);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancellingPrefilledDialogLeavesFiltersUnchanged()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "ERROR");
            var text = FirstText(window);
            text.Focus();
            text.SelectAll();
            window.FindControl<Button>("IncludeButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(((MainWindowViewModel)window.DataContext!).Filters);
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClickingLongLinesPreservesHorizontalOffsetAndAllowsSelection()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, new string('A', 500) + "\n" + new string('B', 500));
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(viewer => ReferenceEquals(viewer.TemplatedParent, list));
            window.MouseMove(new Point(0, 0));
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var texts = list.GetVisualDescendants().OfType<TextBox>().ToArray();
            foreach (var offset in new[] { 0.0, 200.0 })
            {
                scroll.Offset = new Vector(offset, 0);
                window.MouseMove(new Point(0, 0));
                foreach (var text in texts)
                {
                    var start = text.TranslatePoint(new Point(offset + 20, 5), window)!.Value;
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseUp(start, MouseButton.Left);
                    Assert.Equal(offset, scroll.Offset.X, precision: 3);
                    var end = start + new Vector(60, 0);
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                    window.MouseUp(end, MouseButton.Left);
                    Assert.NotEmpty(text.SelectedText);
                    Assert.Equal(offset, scroll.Offset.X, precision: 3);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MouseDragSelectsTextWithoutChangingLogContents()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "ERROR failed");
            var text = FirstText(window);
            window.MouseMove(new Point(0, 0));
            var start = text.TranslatePoint(new Point(1, 5), window)!.Value;
            var end = text.TranslatePoint(new Point(text.Bounds.Width - 1, 5), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Assert.NotEmpty(text.SelectedText);
            window.KeyTextInput("replacement");
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Equal("ERROR failed", text.Text);
        }
        finally { window.Close(); }
    }
}
