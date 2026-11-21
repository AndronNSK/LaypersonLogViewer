using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public sealed class SettingsDialog : Window
{
    public SettingsDialog(AppSettings settings, Action checkNow)
    {
        Title = "Настройки";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var input = new TextBox { Name = "LineLimitInput", Text = settings.LineLimit.ToString() };
        var updates = new CheckBox { Name = "CheckForUpdates", Content = "Проверять обновления", IsChecked = settings.CheckForUpdates };
        var check = new Button { Content = "Проверить сейчас" };
        check.Click += (_, _) => { Close(); checkNow(); };
        AutomationProperties.SetName(input, "Лимит строк");
        var apply = new Button { Name = "ApplyLineLimit", Content = "Применить", IsDefault = true };
        var cancel = new Button { Name = "CancelLineLimit", Content = "Отмена", IsCancel = true };
        bool TryReadLimit(out int limit) => int.TryParse(input.Text, out limit)
            && limit is >= LiveLogBuffer.MinimumCapacity and <= LiveLogBuffer.MaximumCapacity;

        input.TextChanged += (_, _) => apply.IsEnabled = TryReadLimit(out _);
        apply.Click += (_, _) =>
        {
            if (TryReadLimit(out var limit)) Close(new AppSettings(limit, updates.IsChecked == true));
        };
        cancel.Click += (_, _) => Close();
        Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Лимит строк" },
                new TextBlock
                {
                    Text = $"От {LiveLogBuffer.MinimumCapacity:N0} до {LiveLogBuffer.MaximumCapacity:N0} строк. "
                        + "При превышении лимита самые старые строки удаляются.",
                    TextWrapping = TextWrapping.Wrap
                },
                input,
                updates,
                check,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 6,
                    Children = { cancel, apply }
                }
            }
        };
    }
}
