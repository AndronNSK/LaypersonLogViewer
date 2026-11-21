using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class LogSearchUiTests
{
    [AvaloniaFact]
    public async Task MatchingTextAndLineNumbersStayHighlightedIndependentlyOfSelection()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "x and X\nno match\nX at start");
            var model = (MainWindowViewModel)window.DataContext!;
            Click(window, "SearchButton");
            var input = window.FindControl<TextBox>("SearchTextBox")!;
            input.Text = "x";
            window.UpdateLayout();
            var first = Highlight(window, 1);
            var third = Highlight(window, 3);
            Assert.Equal(2, PaintedRanges(first));
            Assert.Equal(0, PaintedRanges(Highlight(window, 2)));
            Assert.Equal(1, PaintedRanges(third));
            Assert.Same(first.NumberBackground, third.NumberBackground);
            Assert.NotNull(first.NumberBackground);
            var number = window.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "LogLineNumber" && border.DataContext is LogLineRow { Number: 1 });
            Assert.Same(first.NumberBackground, number.Background);

            Click(window, "SearchNextButton");
            var text = await Selected(window, 1, 0);
            Assert.NotSame(third.NumberBackground, first.NumberBackground);
            text.SelectionStart = 2;
            text.SelectionEnd = 5;
            Assert.Equal("and", text.SelectedText);
            Assert.Equal(2, PaintedRanges(first));
            window.FindControl<CheckBox>("SearchCaseBox")!.IsChecked = true;
            Assert.Equal(1, PaintedRanges(first));
            Assert.Equal(0, PaintedRanges(third));
            Assert.Null(third.NumberBackground);
            Assert.Equal("and", text.SelectedText);

            await model.ApplyLiveLinesAsync("stream", [..model.AllLines, new(4, "new x")]);
            window.UpdateLayout();
            Assert.Equal(1, PaintedRanges(Highlight(window, 4)));
            Click(window, "CloseSearchButton");
            Assert.Equal(0, PaintedRanges(first));
            Assert.Null(number.Background);
            Assert.Equal("and", text.SelectedText);
        }
        finally { window.Close(); }
    }

    private static LogSearchHighlight Highlight(MainWindow window, int number) =>
        window.GetVisualDescendants().OfType<LogSearchHighlight>()
            .Single(highlight => highlight.Row?.Number == number);

    private static int PaintedRanges(LogSearchHighlight highlight)
    {
        var drawing = new DrawingGroup();
        using (var context = drawing.Open()) highlight.Render(context);
        return CountGeometry(drawing);
    }

    private static int CountGeometry(Drawing drawing) => drawing switch
    {
        GeometryDrawing => 1,
        DrawingGroup group => group.Children.Sum(CountGeometry),
        _ => 0
    };

    private static void Click(MainWindow window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Key(MainWindow window, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }
    private static async Task Load(MainWindow window, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await ((MainWindowViewModel)window.DataContext!).LoadAsync("test.log", stream);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    private static async Task<TextBox> Selected(MainWindow window, int number, int start)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.DataContext is LogLineRow row && row.Number == number
                    && t.SelectionStart == start && t.SelectionEnd > start);
            if (text is not null) return text;
            await Task.Delay(10, timeout.Token);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F3StartsOnClickedLineBeforeContinuingInSearchOrder(bool backwards)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "x before\nno match\nx x\nno match here\nx after");
            Click(window, "SearchButton");
            window.FindControl<TextBox>("SearchTextBox")!.Text = "x";
            Click(window, "SearchNextButton");
            await Selected(window, 1, 0);

            var list = window.FindControl<ListBox>("LogLines")!;
            var row = (ListBoxItem)list.ContainerFromIndex(2)!;
            var target = backwards
                ? (Control)row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "LogLineNumber")
                : row.GetVisualDescendants().OfType<TextBox>().Single();
            var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, 5), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);

            var modifiers = backwards ? RawInputModifiers.Shift : RawInputModifiers.None;
            Key(window, Avalonia.Input.Key.F3, modifiers);
            await Selected(window, 3, backwards ? 2 : 0);
            Key(window, Avalonia.Input.Key.F3, modifiers);
            await Selected(window, 3, backwards ? 0 : 2);
            Key(window, Avalonia.Input.Key.F3, modifiers);
            await Selected(window, backwards ? 1 : 5, 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task KeyboardSearchPrefillsSelectionNavigatesOccurrencesAndCloses()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "x x\nother\nx");
            var list = window.FindControl<ListBox>("LogLines")!;
            var model = (MainWindowViewModel)window.DataContext!;
            Assert.False(window.FindControl<Grid>("SearchBar")!.IsVisible);
            var original = list.GetVisualDescendants().OfType<TextBox>().First();
            original.Focus();
            original.SelectionStart = 0;
            original.SelectionEnd = 1;
            Key(window, Avalonia.Input.Key.F, RawInputModifiers.Control);
            Assert.True(window.FindControl<Grid>("SearchBar")!.IsVisible);
            var input = window.FindControl<TextBox>("SearchTextBox")!;
            Assert.True(input.IsFocused);
            Assert.Equal("x", input.Text);
            Key(window, Avalonia.Input.Key.Enter);
            Assert.Equal("x", (await Selected(window, 1, 0)).SelectedText);
            Key(window, Avalonia.Input.Key.F3);
            await Selected(window, 1, 2);
            Key(window, Avalonia.Input.Key.F3);
            await Selected(window, 3, 0);
            Key(window, Avalonia.Input.Key.F3);
            await Selected(window, 1, 0);
            Assert.Contains("Поиск по кругу", model.Search.Status);
            Key(window, Avalonia.Input.Key.F3, RawInputModifiers.Shift);
            await Selected(window, 3, 0);
            Assert.False(model.Live.FollowLatest);
            Assert.Empty(model.Filters);
            Key(window, Avalonia.Input.Key.Escape);
            Assert.False(window.FindControl<Grid>("SearchBar")!.IsVisible);
            Assert.Equal("x", (await Selected(window, 3, 0)).SelectedText);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchScrollsToVirtualizedLongLineWithoutResumingFollowOrChangingFilters(bool wrap)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.WordWrap = wrap;
            var target = wrap ? new string('я', 180) : "needle";
            await Load(window, string.Join('\n', Enumerable.Range(1, 200).Select(i =>
                i == 150 ? new string('a', 500) + target : $"INFO {i}")));
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(s => ReferenceEquals(s.TemplatedParent, list));
            scroll.Offset = default;
            Click(window, "SearchButton");
            window.FindControl<TextBox>("SearchTextBox")!.Text = target;
            Click(window, "SearchNextButton");
            var text = await Selected(window, 150, 500);
            Assert.Equal(target, text.SelectedText);
            Assert.True(PaintedRanges(Highlight(window, 150)) >= (wrap ? 2 : 1));
            Assert.NotNull(Highlight(window, 150).NumberBackground);
            Assert.True(scroll.Offset.Y > 0);
            if (wrap) Assert.Equal(0, scroll.Offset.X);
            else Assert.True(scroll.Offset.X > 0);
            Assert.False(model.Live.FollowLatest);
            Assert.Equal(200, model.DisplayLines.Count);
            Assert.Empty(model.Filters);
            Click(window, "SearchPreviousButton");
            await WaitFor(() => model.Search.Status.Contains("Поиск по кругу", StringComparison.Ordinal));
            Assert.Equal(target, text.SelectedText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SearchButtonsRespectCaseAndDisplayedFilterScopeAndReportMissingText()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window, "INFO Ошибка\nERROR ошибка");
            var model = (MainWindowViewModel)window.DataContext!;
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            Click(window, "SearchButton");
            var input = window.FindControl<TextBox>("SearchTextBox")!;
            Assert.False(window.FindControl<Button>("SearchNextButton")!.IsEnabled);
            input.Text = "Ошибка";
            window.FindControl<CheckBox>("SearchCaseBox")!.IsChecked = true;
            Click(window, "SearchNextButton");
            await WaitFor(() => model.Search.Status == "Не найдено");
            Assert.Null(Highlight(window, 2).NumberBackground);
            model.ShowFilteredOut = true;
            Click(window, "SearchNextButton");
            await Selected(window, 1, 5);
            Assert.NotNull(Highlight(window, 1).NumberBackground);
            window.FindControl<CheckBox>("SearchCaseBox")!.IsChecked = false;
            Click(window, "SearchPreviousButton");
            Assert.Equal("ошибка", (await Selected(window, 2, 6)).SelectedText);
            Click(window, "CloseSearchButton");
            Assert.False(model.Search.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(760)]
    [InlineData(1120)]
    public void SearchBarFitsWindow(double width)
    {
        var window = new MainWindow { Width = width };
        window.Show();
        try
        {
            Click(window, "SearchButton");
            window.UpdateLayout();
            foreach (var name in new[] { "SearchTextBox", "SearchCaseBox", "SearchPreviousButton", "SearchNextButton", "SearchStatus", "CloseSearchButton" })
            {
                var control = window.FindControl<Control>(name)!;
                Assert.True(control.IsEffectivelyVisible);
                var x = control.TranslatePoint(default, window)!.Value.X;
                Assert.InRange(x, 0, window.Bounds.Width - control.Bounds.Width);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingSearchKeepsSelectionAndInputAcrossArrivalsAndPausesAtBottom(bool wrap)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.WordWrap = wrap;
            var ready = new TaskCompletionSource<Action<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            model.Live.Start("search stream", async (receive, token) =>
            {
                for (var i = 1; i <= 200; i++) receive(i == 100 ? "target found" : $"INFO {i}");
                ready.SetResult(receive);
                await Task.Delay(Timeout.Infinite, token);
                return "done";
            });
            var write = await ready.Task;
            await WaitFor(() => model.AllLines.Count == 200);
            Click(window, "SearchButton");
            var input = window.FindControl<TextBox>("SearchTextBox")!;
            input.Text = "target";
            input.SelectAll();
            Key(window, Avalonia.Input.Key.Enter);
            var matched = await Selected(window, 100, 0);
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(s => ReferenceEquals(s.TemplatedParent, list));
            var offset = scroll.Offset;
            write("arrived during search");
            await WaitFor(() => model.AllLines.Count == 201);
            Assert.Equal("target", matched.SelectedText);
            Assert.Equal("target", input.SelectedText);
            Assert.Equal(offset, scroll.Offset);
            Assert.False(model.Live.FollowLatest);
            write("target at end");
            await WaitFor(() => model.AllLines.Count == 202);
            Key(window, Avalonia.Input.Key.F3);
            await Selected(window, 202, 0);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.Live.FollowLatest);
            offset = scroll.Offset;
            write("must not resume scrolling");
            await WaitFor(() => model.AllLines.Count == 203);
            Assert.False(model.Live.FollowLatest);
            Assert.Equal(offset, scroll.Offset);
            Assert.Equal("target", (await Selected(window, 202, 0)).SelectedText);
        }
        finally { window.Close(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
