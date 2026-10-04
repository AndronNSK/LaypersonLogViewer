using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace LaypersonLogViewer.App.Views;

public sealed record CommandRequest(string Executable, string Arguments, string Directory);

public sealed class CommandDialog : Window
{
    public CommandDialog()
    {
        Title = "Запустить команду";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var executable = new TextBox { Name = "ExecutableBox", PlaceholderText = "Например: dotnet, powershell.exe, /usr/bin/journalctl" };
        var arguments = new TextBox { Name = "ArgumentsBox", PlaceholderText = "Аргументы командной строки" };
        var directory = new TextBox { Name = "DirectoryBox", Text = Environment.CurrentDirectory };
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
