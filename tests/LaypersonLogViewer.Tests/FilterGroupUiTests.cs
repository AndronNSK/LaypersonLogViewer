using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FilterGroupUiTests
{
    private static Button Button(MainWindow window, string name) => window.GetVisualDescendants()
        .OfType<Button>().Single(button => button.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static async Task Settle(MainWindow window)
    {
        var model = (MainWindowViewModel)window.DataContext!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        do { await Task.Delay(10, timeout.Token); } while (model.IsBusy);
        await Dispatcher.UIThread.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Background);
    }

    [AvaloniaTheory]
    [InlineData(FilterKind.Include)]
    [InlineData(FilterKind.Exclude)]
    public async Task ChildDialogPrefillsEditsCancelsAndRemovesConditions(FilterKind kind)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.ShowFilteredOut = true;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ERROR database\nERROR other"));
            await model.LoadAsync("test.log", stream);
            await model.AddFilterAsync(new LogFilter(kind, "ERROR"));
            await Settle(window);
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().First();
            text.Focus();
            text.SelectionStart = 6;
            text.SelectionEnd = 14;
            Click(Button(window, "AddConditionButton"));
            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal("database", dialog.FindControl<TextBox>("PatternBox")!.Text);
            Click(dialog.FindControl<Button>("CancelButton")!);
            await Settle(window);
            Assert.Empty(model.Filters[0].AdditionalConditions);

            Click(Button(window, "AddConditionButton"));
            dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            dialog.KeyTextInput("data");
            Assert.Equal("data", dialog.FindControl<TextBox>("PatternBox")!.Text);
            dialog.FindControl<CheckBox>("CaseSensitiveBox")!.IsChecked = true;
            Click(dialog.FindControl<Button>("AddButton")!);
            await Settle(window);
            var condition = Assert.Single(Assert.Single(model.Filters).AdditionalConditions);
            Assert.Equal("data", condition.Text);
            Assert.True(condition.CaseSensitive);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "И содержит");
            text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal("database", text.SelectedText);

            Click(Button(window, "RemoveConditionButton"));
            await Settle(window);
            Assert.Empty(model.Filters[0].AdditionalConditions);
            await model.AddConditionAsync(model.Filters[0], new LogFilterCondition("database"));
            await Settle(window);
            Click(Button(window, "RemoveFilterButton"));
            await Settle(window);
            Assert.Empty(model.Filters);
            Assert.Equal(2, model.VisibleLines.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChildDialogWithoutSelectionStartsEmpty()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await ((MainWindowViewModel)window.DataContext!).AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            await Settle(window);
            Click(Button(window, "AddConditionButton"));
            var dialog = Assert.IsType<FilterDialog>(Assert.Single(window.OwnedWindows));
            Assert.True(string.IsNullOrEmpty(dialog.FindControl<TextBox>("PatternBox")!.Text));
            Assert.False(dialog.FindControl<Button>("AddButton")!.IsEnabled);
            Click(dialog.FindControl<Button>("CancelButton")!);
        }
        finally { window.Close(); }
    }
}
