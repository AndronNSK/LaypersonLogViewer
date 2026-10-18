using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class LogSearchUiTests
{
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
            await Load(window, string.Join('\n', Enumerable.Range(1, 200).Select(i =>
                i == 150 ? new string('a', 500) + "needle" : $"INFO {i}")));
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(s => ReferenceEquals(s.TemplatedParent, list));
            scroll.Offset = default;
            Click(window, "SearchButton");
            window.FindControl<TextBox>("SearchTextBox")!.Text = "needle";
            Click(window, "SearchNextButton");
            var text = await Selected(window, 150, 500);
            Assert.Equal("needle", text.SelectedText);
            Assert.True(scroll.Offset.Y > 0);
            if (wrap) Assert.Equal(0, scroll.Offset.X);
            else Assert.True(scroll.Offset.X > 0);
            Assert.False(model.Live.FollowLatest);
            Assert.Equal(200, model.DisplayLines.Count);
            Assert.Empty(model.Filters);
            Click(window, "SearchPreviousButton");
            await WaitFor(() => model.Search.Status.Contains("Поиск по кругу", StringComparison.Ordinal));
            Assert.Equal("needle", text.SelectedText);
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
            model.ShowFilteredOut = true;
            Click(window, "SearchNextButton");
            await Selected(window, 1, 5);
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
    public void SearchBarAndLargeLineLimitFitWindow(double width)
    {
        var window = new MainWindow { Width = width };
        window.Show();
        try
        {
            var limit = window.FindControl<NumericUpDown>("RetainedLinesBox")!;
            Assert.Equal(5_000_000, limit.Value);
            Assert.Equal(20_000_000, limit.Maximum);
            limit.Value = 20_000_000;
            Assert.Equal(20_000_000, ((MainWindowViewModel)window.DataContext!).Live.RetainedLines);
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
