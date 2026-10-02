using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            var item = Assert.IsType<LogLine>(Assert.Single(list.Items));
            Assert.Equal(2, item.Number);
            Assert.Equal("ERROR failed", item.Text);
        }
        finally { window.Close(); }
    }
}
