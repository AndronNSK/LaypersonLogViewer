using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class TimestampSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task FirstMenuItemAppliesTimestampWithoutDialogAndOpensDetailsTab(bool reverse, bool collapsed)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            const string stamp = "2026-10-03 12:34:56";
            var model = (MainWindowViewModel)window.DataContext!;
            await Load(model, $"[{stamp}] ERROR failed\n stack trace\n[2026-10-03 12:34:57] INFO ready");
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            if (collapsed) Click(window, "LowerPaneToggle");
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().First();
            text.Focus();
            text.SelectionStart = reverse ? 1 + stamp.Length : 1;
            text.SelectionEnd = reverse ? 1 : 1 + stamp.Length;
            var menu = OpenMenu(window, text);
            var item = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal("Начало записи по времени", item.Header);
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Empty(window.OwnedWindows);
            await WaitFor(() => model.HasTimestampPattern && !model.IsBusy);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Empty(window.OwnedWindows);
            Assert.False(menu.IsOpen);
            Assert.Equal(1, model.TimestampPattern!.StartIndex);
            Assert.Equal(stamp, model.TimestampPattern.Example);
            Assert.Equal(2, model.TimestampStartCount);
            Assert.Equal(2, model.EntryCount);
            Assert.Equal(new[] { 1, 2 }, model.DisplayLines.Select(row => row.Number));
            Assert.Equal(stamp, text.SelectedText);
            var tab = window.FindControl<TabItem>("TimestampTab")!;
            Assert.Same(tab, window.FindControl<TabControl>("LowerTabs")!.SelectedItem);
            Assert.True(window.FindControl<Border>("LowerPane")!.IsVisible);
            Assert.Equal(stamp, window.FindControl<TextBox>("TimestampExample")!.Text);
            Assert.Contains("Позиция: 2", window.FindControl<TextBlock>("TimestampDescription")!.Text);
            Assert.Contains("Начал найдено: 2", window.FindControl<TextBlock>("TimestampDescription")!.Text);
            Assert.Equal("Записей подходит: 1 из 2", window.FindControl<TextBlock>("TimestampEntrySummary")!.Text);
            Assert.DoesNotContain("Записей подходит", model.Status);
            var reset = window.FindControl<Button>("ResetTimestampButton")!;
            Assert.Contains(reset, tab.GetLogicalDescendants());
            Assert.DoesNotContain(reset, window.FindControl<WrapPanel>("SourceToolbar")!.GetVisualDescendants());
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => !model.HasTimestampPattern && !model.IsBusy);
            window.UpdateLayout();
            Assert.Single(model.DisplayLines);
            Assert.Single(model.Filters);
            Assert.Same(tab, window.FindControl<TabControl>("LowerTabs")!.SelectedItem);
            Assert.False(reset.IsVisible);
            Assert.Contains("Построчно", window.FindControl<TextBlock>("TimestampDescription")!.Text);
            Assert.Empty(model.TimestampEntrySummary);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 7)]
    public async Task TimestampActionStaysFirstButIsDisabledWithoutNumericSelection(int start, int end)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            await Load(model, "   INFO ready");
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().First();
            text.Focus();
            text.SelectionStart = start;
            text.SelectionEnd = end;
            var menu = OpenMenu(window, text);
            var item = Assert.IsType<MenuItem>(menu.Items[0]);
            Assert.Equal("Начало записи по времени", item.Header);
            Assert.False(item.IsEnabled);
            Assert.False(model.HasTimestampPattern);
            Assert.Empty(window.OwnedWindows);
            menu.Close();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TimestampTabUpdatesCountsForIncomingLinesAndContainsNoMatchWarning()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            await Load(model, "12:00 INFO\n detail");
            await model.SetTimestampPatternAsync(new TimestampPattern("12:00", 0));
            var tab = window.FindControl<TabItem>("TimestampTab")!;
            window.FindControl<TabControl>("LowerTabs")!.SelectedItem = tab;
            await model.ApplyLiveLinesAsync("stream", [.. model.AllLines, new(3, "12:01 ERROR")]);
            window.UpdateLayout();
            Assert.Contains("Начал найдено: 2", window.FindControl<TextBlock>("TimestampDescription")!.Text);
            Assert.Equal("Записей подходит: 2 из 2", window.FindControl<TextBlock>("TimestampEntrySummary")!.Text);
            await Load(model, "no matching timestamp\n another line");
            window.UpdateLayout();
            var warning = window.FindControl<TextBlock>("TimestampWarning")!;
            Assert.True(warning.IsEffectivelyVisible);
            Assert.Contains(warning, tab.GetLogicalDescendants());
            Assert.Contains("Шаблон не найден", warning.Text);
            Assert.Contains("Начал найдено: 0", window.FindControl<TextBlock>("TimestampDescription")!.Text);
            Click(window, "ResetTimestampButton");
            await WaitFor(() => !model.HasTimestampPattern && !model.IsBusy);
            Assert.False(warning.IsVisible);
            Assert.Equal(2, model.DisplayLines.Count);
        }
        finally { window.Close(); }
    }

    private static void Click(MainWindow window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static ContextMenu OpenMenu(MainWindow window, TextBox text)
    {
        window.MouseMove(default);
        var point = text.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        return text.ContextMenu!;
    }
    private static async Task Load(MainWindowViewModel model, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await model.LoadAsync("test.log", stream);
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
