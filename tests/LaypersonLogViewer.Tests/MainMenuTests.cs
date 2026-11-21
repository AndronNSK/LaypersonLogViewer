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

namespace LaypersonLogViewer.Tests;

public sealed class MainMenuTests
{
    [AvaloniaFact]
    public void HeaderIconOpensClassicSubmenusAboveTheToolbar()
    {
        var window = new MainWindow { Width = 760, Height = 560 };
        window.Show();
        try
        {
            var button = window.FindControl<Button>("MainMenuButton")!;
            var toolbar = window.FindControl<Grid>("MainToolbar")!;
            window.MouseMove(default);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            Assert.True(point.Y < toolbar.TranslatePoint(default, window)!.Value.Y);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.True(menu.IsOpen);
            Assert.Collection(menu.Items.OfType<MenuItem>(),
                item => { Assert.Equal("_Файл", item.Header); Assert.Contains("Открыть файл…", item.Items.OfType<MenuItem>().Select(child => child.Header)); },
                item => { Assert.Equal("_Вид", item.Header); Assert.Contains("Найти…", item.Items.OfType<MenuItem>().Select(child => child.Header)); },
                item => { Assert.Equal("_Справка", item.Header); Assert.Contains("О программе", item.Items.OfType<MenuItem>().Select(child => child.Header)); });
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FileMenuContainsTheOnlyStopSaveAndLimitControls()
    {
        var window = new MainWindow { Width = 760, Height = 560 };
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            var toolbar = window.FindControl<WrapPanel>("SourceToolbar")!;
            Assert.DoesNotContain(toolbar.Children, child => child is NumericUpDown
                || child is Button { Content: "Стоп" or "Сохранить журнал…" });
            var file = OpenFileMenu(window);
            var stop = FindItem(file, "Стоп");
            Assert.True(FindItem(file, "Сохранить журнал…").IsEnabled);
            Assert.True(FindItem(file, "Настройки…").IsEnabled);
            Assert.False(stop.IsEnabled);
            model.Live.Start("test", async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "done";
            });
            Assert.True(stop.IsEnabled);
            stop.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await model.Live.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(model.Live.IsActive);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LineLimitDialogCommitsOnlyWhenApplied(bool apply)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            var file = OpenFileMenu(window);
            FindItem(file, "Настройки…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = Assert.IsType<SettingsDialog>(Assert.Single(window.OwnedWindows));
            var input = dialog.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "LineLimitInput");
            Assert.True(dialog.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);
            dialog.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = false;
            var applyButton = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplyLineLimit");
            Assert.Equal("5000000", input.Text);

            foreach (var invalid in new[] { "", "99", "20000001", "123.5" })
            {
                input.Text = invalid;
                Dispatcher.UIThread.RunJobs();
                Assert.False(applyButton.IsEnabled);
            }
            input.Text = "20000000";
            Dispatcher.UIThread.RunJobs();
            Assert.True(applyButton.IsEnabled);
            Assert.Equal(5_000_000, model.Live.RetainedLines);
            var action = apply ? applyButton : dialog.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "CancelLineLimit");
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(apply ? 20_000_000 : 5_000_000, model.Live.RetainedLines);
            FindItem(file, "Настройки…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var reopened = Assert.IsType<SettingsDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal(!apply, reopened.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);
            reopened.Close();
        }
        finally { window.Close(); }
    }

    private static MenuItem OpenFileMenu(MainWindow window)
    {
        var button = window.FindControl<Button>("MainMenuButton")!;
        var menu = Assert.IsType<MenuFlyout>(button.Flyout);
        menu.ShowAt(button);
        var file = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Файл"));
        file.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        return file;
    }

    private static MenuItem FindItem(MenuItem parent, string header) =>
        parent.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    [AvaloniaFact]
    public void HelpMenuOpensAboutWithTheProjectLink()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var button = window.FindControl<Button>("MainMenuButton")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            menu.ShowAt(button);
            var help = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Справка"));
            help.IsSubMenuOpen = true;
            var about = FindItem(help, "О программе");
            menu.Hide();
            about.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var dialog = Assert.Single(window.OwnedWindows);
            Assert.Equal("О программе", dialog.Title);
            var link = Assert.Single(dialog.GetVisualDescendants().OfType<HyperlinkButton>());
            Assert.Equal("https://github.com/AndronNSK/LaypersonLogViewer", link.NavigateUri?.AbsoluteUri);
            dialog.Close();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ViewMenuUsesTheExistingSearchAndDisplayControls()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var button = window.FindControl<Button>("MainMenuButton")!;
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            menu.ShowAt(button);
            var view = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "_Вид"));
            view.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var model = (MainWindowViewModel)window.DataContext!;
            var wrap = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Перенос строк"));
            wrap.IsChecked = true;
            Assert.True(model.WordWrap);
            Assert.True(window.FindControl<CheckBox>("WordWrapBox")!.IsChecked);

            var grey = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Показывать отфильтрованные серым"));
            model.ShowFilteredOut = true;
            Assert.True(grey.IsChecked);

            var pane = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Панель вкладок"));
            Assert.True(pane.IsChecked);
            pane.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.False(window.FindControl<Border>("LowerPane")!.IsVisible);
            Assert.False(pane.IsChecked);

            var find = view.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Найти…"));
            find.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(model.Search.IsVisible);
        }
        finally { window.Close(); }
    }
}
