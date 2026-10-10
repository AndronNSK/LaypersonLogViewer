using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

[assembly: AvaloniaTestApplication(typeof(LaypersonLogViewer.Tests.TestAppBuilder))]

namespace LaypersonLogViewer.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<LaypersonLogViewer.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class UiTests
{
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task HidingLowerPaneExpandsLogAndRestoresSizeTabAndPatterns()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            model.Statistics.SavePattern(StatisticsTests.Pattern());
            var tabs = window.FindControl<TabControl>("LowerTabs")!;
            tabs.SelectedIndex = 1;
            var grid = window.FindControl<Grid>("MainLayout")!;
            grid.RowDefinitions[3].Height = new GridLength(260);
            window.UpdateLayout();
            var log = window.FindControl<ListBox>("LogLines")!;
            var toggle = window.FindControl<Button>("LowerPaneToggle")!;
            var pane = window.FindControl<Border>("LowerPane")!;
            var divider = window.FindControl<GridSplitter>("LowerPaneDivider")!;
            window.MouseMove(new Point(0, 0));
            var dragStart = divider.TranslatePoint(new Point(40, 2), window)!.Value;
            var paneHeight = pane.Bounds.Height;
            window.MouseDown(dragStart, Avalonia.Input.MouseButton.Left);
            window.MouseMove(dragStart + new Vector(0, -30), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(dragStart + new Vector(0, -30), Avalonia.Input.MouseButton.Left);
            window.UpdateLayout();
            Assert.True(pane.Bounds.Height > paneHeight);
            var savedHeight = grid.RowDefinitions[3].Height;
            var initialHeight = log.Bounds.Height;
            for (var i = 0; i < 2; i++)
            {
                var arrowPoint = toggle.TranslatePoint(new Point(18, 6), window)!.Value;
                window.MouseDown(arrowPoint, Avalonia.Input.MouseButton.Left);
                window.MouseUp(arrowPoint, Avalonia.Input.MouseButton.Left);
                window.UpdateLayout();
                Assert.False(pane.IsVisible);
                Assert.False(divider.IsEnabled);
                Assert.True(toggle.IsEffectivelyVisible);
                Assert.Equal("▴", toggle.Content);
                Assert.True(log.Bounds.Height > initialHeight + 200);
                Assert.InRange(grid.RowDefinitions[3].ActualHeight, 0, grid.RowSpacing);
                Click(toggle);
                window.UpdateLayout();
                Assert.True(pane.IsVisible);
                Assert.True(divider.IsVisible);
                Assert.True(divider.IsEnabled);
                Assert.Equal("▾", toggle.Content);
                Assert.Equal(savedHeight, grid.RowDefinitions[3].Height);
                Assert.Equal(initialHeight, log.Bounds.Height, precision: 3);
                Assert.Equal(1, tabs.SelectedIndex);
                Assert.Single(model.Filters);
                Assert.Single(model.Statistics.Patterns);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DisplaySwitchRestoresContextAndDimsOnlyFilteredOutRows()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("INFO ready\nERROR failed"));
            await model.LoadAsync("test.log", stream);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            var list = window.FindControl<ListBox>("LogLines")!;
            var toggle = window.FindControl<ToggleSwitch>("ShowFilteredOutSwitch")!;
            Assert.Single(list.Items);

            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(model.ShowFilteredOut);
            Assert.Equal(2, list.Items.Count);
            var rows = list.GetVisualDescendants().OfType<Grid>()
                .Where(grid => grid.Name == "LineContent").OrderBy(grid => ((LogLineRow)grid.DataContext!).Number).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(0.4, rows[0].Opacity);
            Assert.Equal(1.0, rows[1].Opacity);

            toggle.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.ShowFilteredOut);
            Assert.Equal(2, Assert.IsType<LogLineRow>(Assert.Single(list.Items)).Number);
            Assert.Single(model.Filters);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("IncludeButton", FilterKind.Include)]
    [InlineData("ExcludeButton", FilterKind.Exclude)]
    public async Task MainWindowButtonsOpenDialogsAndAddCorrectFilter(string buttonName, FilterKind expectedKind)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            Click(window.FindControl<Button>(buttonName)!);
            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            var add = dialog.FindControl<Button>("AddButton")!;
            Assert.False(add.IsEnabled);
            dialog.FindControl<TextBox>("PatternBox")!.Text = "   ";
            Dispatcher.UIThread.RunJobs();
            Assert.False(add.IsEnabled);
            dialog.FindControl<TextBox>("PatternBox")!.Text = "ERROR";
            dialog.FindControl<CheckBox>("CaseSensitiveBox")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(add.IsEnabled);
            Click(add);
            var model = (MainWindowViewModel)window.DataContext!;
            // Dialog continuations and background filtering complete asynchronously.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (model.Filters.Count == 0 || model.IsBusy)
                await Task.Delay(10, timeout.Token);
            var filter = Assert.Single(model.Filters);
            Assert.Equal(expectedKind, filter.Kind);
            Assert.Equal("ERROR", filter.Text);
            Assert.True(filter.CaseSensitive);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CancelReturnsNoFilter()
    {
        var owner = new MainWindow();
        owner.Show();
        try
        {
            var dialog = new FilterDialog(FilterKind.Exclude);
            var result = dialog.ShowDialog<LogFilter?>(owner);
            dialog.FindControl<TextBox>("PatternBox")!.Text = "ERROR";
            Click(dialog.FindControl<Button>("CancelButton")!);
            Assert.Null(await result);
            Assert.Empty(((MainWindowViewModel)owner.DataContext!).Filters);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task LogListBindingShowsOnlyFilteredLines()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("INFO ready\nERROR failed"));
            await model.LoadAsync("test.log", stream);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "error"));
            Dispatcher.UIThread.RunJobs();
            var list = window.FindControl<ListBox>("LogLines")!;
            var item = Assert.IsType<LogLineRow>(Assert.Single(list.Items));
            Assert.Equal(2, item.Number);
            Assert.Equal("ERROR failed", item.Text);
        }
        finally { window.Close(); }
    }
}
