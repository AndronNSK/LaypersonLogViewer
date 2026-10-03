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

public sealed class TextSelectionTests
{
    private static async Task Load(MainWindow window, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await ((MainWindowViewModel)window.DataContext!).LoadAsync("test.log", stream);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static SelectableTextBlock FirstText(MainWindow window) =>
        window.FindControl<ListBox>("LogLines")!.GetVisualDescendants()
            .OfType<SelectableTextBlock>().First();

    private static ContextMenu OpenMenu(MainWindow window, SelectableTextBlock text)
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
    public async Task SelectedTextPrefillsCorrectFilterDialog(FilterKind kind, bool greyMode)
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
            var item = menu.Items.OfType<MenuItem>().ElementAt(kind == FilterKind.Include ? 0 : 1);
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal(selected, dialog.FindControl<TextBox>("PatternBox")!.Text);
            Assert.Equal(countBefore, model.Filters.Count);
            var add = dialog.FindControl<Button>("AddButton")!;
            Assert.True(add.IsEnabled);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            Assert.All(menu.Items.OfType<MenuItem>(), item => Assert.False(item.IsEnabled));
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
            var menu = OpenMenu(window, text);
            menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
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
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            window.MouseMove(new Point(0, 0));
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var texts = list.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
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
            Assert.Equal("ERROR failed", text.Text);
        }
        finally { window.Close(); }
    }
}
