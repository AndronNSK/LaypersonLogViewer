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
    [InlineData(true, FilterKind.Include)]
    [InlineData(true, FilterKind.Exclude)]
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
                menu.Items.OfType<MenuItem>().ElementAt(kind == FilterKind.Include ? 0 : 1)
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
    [InlineData(true, false, false, true)]
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
                menu.Items.OfType<MenuItem>().ElementAt(exclude ? 1 : 0)
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
            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            // A toolbar exclusion removes another row; the context exclusion removes this row.
            dialog.FindControl<TextBox>("PatternBox")!.Text = exclude && !contextMenu ? "before" : "ERROR";
            dialog.FindControl<Button>(cancel ? "CancelButton" : "AddButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
