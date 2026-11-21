using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowDecorationMarginProperty && MainMenuButton is not null)
            UpdateMenuLayout();
    }

    private void UpdateMenuLayout()
    {
        var frame = WindowDecorationMargin;
        var titleBarHeight = ExtendClientAreaTitleBarHeightHint;
        // Keep an in-window header for platforms that supply their own title bar.
        var headerTop = Math.Max(0, frame.Top - titleBarHeight);
        MainContent.Padding = new Thickness(frame.Left, headerTop + titleBarHeight, frame.Right, frame.Bottom);
        MainMenuButton.Margin = new Thickness(frame.Left + 7, headerTop + 3, 0, 0);
    }

    private void ExitMenu_Click(object? sender, RoutedEventArgs e) => Close();

    private async void SettingsMenu_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settings with { LineLimit = ViewModel.Live.RetainedLines };
        if (await new SettingsDialog(settings, () => _ = CheckUpdatesAsync(true)).ShowDialog<AppSettings?>(this) is not { } accepted)
            return;
        _settings = accepted;
        ViewModel.Live.RetainedLines = accepted.LineLimit;
        try { if (_settingsStore is not null) await _settingsStore.SaveAsync(accepted); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ViewModel.ReportError("Не удалось сохранить настройки. " + error.Message); }
    }

    private async void KeyboardShortcutsMenu_Click(object? sender, RoutedEventArgs e) =>
        await ShowMenuInfoAsync("Горячие клавиши",
            "Ctrl+F — открыть поиск\nF3 — следующее совпадение\nShift+F3 — предыдущее совпадение\nEsc — закрыть меню или поиск");

    private async void AboutMenu_Click(object? sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        var projectLink = new HyperlinkButton
        {
            Content = "Проект на GitHub ↗",
            NavigateUri = new Uri("https://github.com/AndronNSK/LaypersonLogViewer"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(0)
        };
        await ShowMenuInfoAsync("О программе", $"Layperson Log Viewer\nВерсия {version}", projectLink);
    }

    private Task ShowMenuInfoAsync(string title, string message, Control? detail = null)
    {
        var close = new Button { Content = "Закрыть", IsDefault = true, IsCancel = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 10,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }
        };
        if (detail is not null) content.Children.Add(detail);
        content.Children.Add(close);
        var dialog = new Window
        {
            Title = title,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = content
        };
        close.Click += (_, _) => dialog.Close();
        return dialog.ShowDialog(this);
    }
}
