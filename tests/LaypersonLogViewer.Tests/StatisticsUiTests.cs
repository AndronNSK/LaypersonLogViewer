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

public sealed class StatisticsUiTests
{
    [AvaloniaFact]
    public void PatternFileButtonsFitAtMinimumWindowWidth()
    {
        var window = new MainWindow { Width = 760, Height = 560 };
        window.Show();
        try
        {
            window.MouseMove(new Point(0, 0));
            window.UpdateLayout();
            foreach (var name in new[] { "LoadFiltersButton", "SaveFiltersButton" })
                CheckButton(window.FindControl<Button>(name)!);
            window.FindControl<TabControl>("LowerTabs")!.SelectedIndex = 1;
            window.UpdateLayout();
            window.MouseMove(new Point(0, 0));
            var panel = window.GetVisualDescendants().OfType<StatisticsPanel>().Single();
            foreach (var name in new[] { "LoadStatisticsButton", "SaveStatisticsButton" })
                CheckButton(panel.FindControl<Button>(name)!);
            Assert.Empty(((MainWindowViewModel)window.DataContext!).Statistics.Patterns);

            void CheckButton(Button button)
            {
                Assert.True(button.IsEffectivelyVisible);
                Assert.True(button.IsEnabled);
                Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
                var topLeft = button.TranslatePoint(default, window)!.Value;
                Assert.InRange(topLeft.X, 0, window.ClientSize.Width - button.Bounds.Width);
                Assert.InRange(topLeft.Y, 0, window.ClientSize.Height - button.Bounds.Height);
                var hit = window.InputHitTest(button.TranslatePoint(
                    new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value) as Visual;
                Assert.Same(button, hit?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault());
            }
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Load(MainWindow window)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("duration=12 size=20\nduration=24 size=40"));
        await ((MainWindowViewModel)window.DataContext!).LoadAsync("test.log", stream);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task ContextMenuUsesWholeLineAndBuilderCreatesTwoValuePattern()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window);
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().First();
            text.Focus();
            text.SelectionStart = 9;
            text.SelectionEnd = 11;
            window.MouseMove(new Point(0, 0));
            var point = text.TranslatePoint(new Point(10, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            var menu = text.ContextMenu!;
            menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Создать статистику…"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = Assert.IsType<StatisticsPatternDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal("duration=12 size=20", dialog.Model.Example);
            var example = dialog.FindControl<TextBox>("ExampleBox")!;
            example.SelectionStart = 0;
            example.SelectionEnd = 9;
            Click(dialog.FindControl<Button>("AnchorButton")!);
            foreach (var (start, end, name) in new[] { (9, 11, "Время"), (17, 19, "Размер") })
            {
                example.SelectionStart = start;
                example.SelectionEnd = end;
                Click(dialog.FindControl<Button>("NumberButton")!);
                var prompt = Assert.IsType<StatisticsPromptDialog>(Assert.Single(dialog.OwnedWindows));
                var input = prompt.GetVisualDescendants().OfType<TextBox>().Single();
                input.Text = name;
                Dispatcher.UIThread.RunJobs();
                Click(prompt.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton"));
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            Assert.True(dialog.Model.CanSave);
            await dialog.Model.PreviewTask;
            Assert.Contains("Время: 12", dialog.Model.PreviewText);
            Click(dialog.FindControl<Button>("SaveButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var model = (MainWindowViewModel)window.DataContext!;
            await model.Statistics.CurrentCalculation;
            var row = Assert.Single(model.Statistics.Patterns);
            Assert.Equal(2, row.Values.Count);
            Assert.Equal("18", row.Values[0].Mean);
            Assert.Equal("30", row.Values[1].Mean);
            Assert.Equal(1, window.FindControl<TabControl>("LowerTabs")!.SelectedIndex);
            window.UpdateLayout();
            var panel = window.GetVisualDescendants().OfType<StatisticsPanel>().Single();
            Assert.True(panel.FindControl<Button>("EditPatternButton")!.IsEnabled);
            Click(panel.FindControl<Button>("DeletePatternButton")!);
            Assert.Empty(model.Statistics.Patterns);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ToolbarManualRegexEditingCancelAndScopeSelection()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            await Load(window);
            window.FindControl<TabControl>("LowerTabs")!.SelectedIndex = 1;
            window.UpdateLayout();
            var panel = window.GetVisualDescendants().OfType<StatisticsPanel>().Single();
            Click(panel.FindControl<Button>("AddPatternButton")!);
            var dialog = Assert.IsType<StatisticsPatternDialog>(Assert.Single(window.OwnedWindows));
            Assert.True(dialog.Model.ManualRegex);
            var regex = dialog.FindControl<TextBox>("RegexBox")!;
            regex.Focus();
            dialog.KeyTextInput(@"duration=(?<duration>\d+)");
            Assert.True(dialog.Model.CanSave);
            Click(dialog.FindControl<Button>("SaveButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var stats = ((MainWindowViewModel)window.DataContext!).Statistics;
            await stats.CurrentCalculation;
            Click(panel.FindControl<Button>("EditPatternButton")!);
            dialog = Assert.IsType<StatisticsPatternDialog>(Assert.Single(window.OwnedWindows));
            dialog.Model.Name = "Discard this";
            Click(dialog.FindControl<Button>("CancelButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.NotEqual("Discard this", stats.Patterns[0].Name);
            panel.FindControl<ComboBox>("ScopeBox")!.SelectedIndex = 1;
            Assert.Equal(1, stats.ScopeIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PatternRowsCanBeSelectedByMouseAndEditedWithoutAddingAnotherRow()
    {
        var window = new MainWindow { Width = 760 };
        window.Show();
        try
        {
            await Load(window);
            var stats = ((MainWindowViewModel)window.DataContext!).Statistics;
            var first = StatisticsTests.Pattern(@"duration=(?<x>\d+)") with { Name = "Duration" };
            stats.SavePattern(first);
            stats.SavePattern(StatisticsTests.Pattern(@"size=(?<x>\d+)") with { Name = "Size" });
            await stats.CurrentCalculation;
            window.FindControl<TabControl>("LowerTabs")!.SelectedIndex = 1;
            window.UpdateLayout();
            var list = window.GetVisualDescendants().OfType<StatisticsPanel>().Single().FindControl<ListBox>("PatternsList")!;
            window.MouseMove(new Point(0, 0));
            list.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(scroll => ReferenceEquals(scroll.TemplatedParent, list)).Offset = default;
            window.MouseMove(new Point(0, 0));
            var title = window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Duration");
            var point = title.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(first.Id == stats.SelectedPattern!.Pattern.Id,
                $"Click at {point}; hit {window.InputHitTest(point)?.GetType().Name}");
            var panel = window.GetVisualDescendants().OfType<StatisticsPanel>().Single();
            Click(panel.FindControl<Button>("EditPatternButton")!);
            var dialog = Assert.IsType<StatisticsPatternDialog>(Assert.Single(window.OwnedWindows));
            dialog.Model.Name = "Elapsed";
            dialog.Model.Expression = @"size=(?<x>\d+)";
            Click(dialog.FindControl<Button>("SaveButton")!);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await stats.CurrentCalculation;
            Assert.Equal(2, stats.Patterns.Count);
            Assert.Equal("Elapsed", stats.Patterns[0].Name);
            Assert.Equal("30", stats.Patterns[0].Values[0].Mean);
            Assert.Equal(first.Id, stats.Patterns[0].Pattern.Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReturningToBuilderRequiresConfirmation()
    {
        var owner = new MainWindow();
        owner.Show();
        var dialog = new StatisticsPatternDialog("x=2", StatisticsTests.Lines("x=2"));
        try
        {
            var completion = dialog.ShowDialog<StatisticsPattern?>(owner);
            dialog.Model.AddRange(0, 2, StatisticsRangeKind.Anchor);
            dialog.Model.AddRange(2, 1, StatisticsRangeKind.Number, "x");
            var manual = dialog.FindControl<CheckBox>("ManualRegexBox")!;
            manual.IsChecked = true;
            manual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog.Model.Expression = @"changed=(?<x>\d+)";
            manual.IsChecked = false;
            manual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var prompt = Assert.IsType<StatisticsPromptDialog>(Assert.Single(dialog.OwnedWindows));
            Click(prompt.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CancelButton"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.True(dialog.Model.ManualRegex);
            Assert.True(manual.IsChecked);
            manual.IsChecked = false;
            manual.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            prompt = Assert.IsType<StatisticsPromptDialog>(Assert.Single(dialog.OwnedWindows));
            Click(prompt.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton"));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.False(dialog.Model.ManualRegex);
            Assert.StartsWith("x=", dialog.Model.Expression);
            Click(dialog.FindControl<Button>("CancelButton")!);
            Assert.Null(await completion);
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
