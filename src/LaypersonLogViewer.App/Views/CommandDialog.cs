using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public sealed class CommandDialog : Window
{
    public CommandDialog(IReadOnlyList<CommandRequest>? history = null)
    {
        Title = "Запустить команду";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var executable = new TextBox { Name = "ExecutableBox", PlaceholderText = "Например: dotnet, powershell.exe, /usr/bin/journalctl" };
        var arguments = new TextBox { Name = "ArgumentsBox", PlaceholderText = "Аргументы командной строки" };
        var directory = new TextBox { Name = "DirectoryBox", Text = Environment.CurrentDirectory };
        var recent = new ComboBox
        {
            Name = "CommandHistoryBox", ItemsSource = history?.ToArray() ?? [],
            PlaceholderText = "Выберите предыдущую команду", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<CommandRequest>((command, _) => new TextBlock
            {
                Text = command is null ? "" : command.Executable + " " + command.Arguments,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                [ToolTip.TipProperty] = command is null ? "" : command.Executable + " " + command.Arguments + "\n" + command.Directory
            })
        };
        recent.SelectionChanged += (_, _) =>
        {
            if (recent.SelectedItem is not CommandRequest command) return;
            executable.Text = command.Executable;
            arguments.Text = command.Arguments;
            directory.Text = command.Directory;
        };
        var run = new Button { Name = "RunCommandButton", Content = "Запустить", IsEnabled = false };
        executable.TextChanged += (_, _) => run.IsEnabled = !string.IsNullOrWhiteSpace(executable.Text);
        run.Click += (_, _) => Close(new CommandRequest(executable.Text!.Trim(), arguments.Text ?? "", directory.Text ?? ""));
        var cancel = new Button { Name = "CancelCommandButton", Content = "Отмена" };
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(12), Spacing = 6,
            Children =
            {
                new StackPanel { Spacing = 4, IsVisible = history is { Count: > 0 }, Children =
                    { new TextBlock { Text = "Недавние команды" }, recent } },
                new TextBlock { Text = "Программа" }, executable,
                new TextBlock { Text = "Аргументы" }, arguments,
                new TextBlock { Text = "Рабочая папка" }, directory,
                new TextBlock { Text = "Вывод и ошибки: UTF-8. Ввод не поддерживается. «Стоп» завершает запущенный процесс.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 6, Children = { run, cancel } }
            }
        };
    }
}
