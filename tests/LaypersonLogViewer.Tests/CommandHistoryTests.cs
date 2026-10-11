using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class CommandHistoryTests
{
    private static T Find<T>(CommandDialog dialog, string name) where T : Control =>
        dialog.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private sealed class TemporaryHistory : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string File => Path.Combine(Folder, "history.json");
        public void Dispose() { if (Directory.Exists(Folder)) Directory.Delete(Folder, true); }
    }

    [Fact]
    public async Task SavedHistoryRestoresUnicodeArgumentsQuotesAndWorkingDirectory()
    {
        using var file = new TemporaryHistory();
        var history = new CommandHistory(file.File);
        var first = new CommandRequest("generator", "--file \"журнал приложения.log\"", "папка журнала");
        var second = first with { Arguments = "--interval-ms 10" };
        await history.RememberAsync(first);
        await history.RememberAsync(second);
        var restored = new CommandHistory(file.File);
        await restored.LoadAsync();
        Assert.Equal(new[] { second, first }, restored.Entries);
        Assert.Empty(Directory.GetFiles(file.Folder, "*.tmp"));
    }

    [Fact]
    public async Task RepeatedCommandsMoveToTopWhileDifferentArgumentsAndDirectoriesRemain()
    {
        var history = new CommandHistory();
        var first = new CommandRequest("generator", "--count 10", "logs");
        var second = first with { Arguments = "--count 20" };
        var third = first with { Directory = "other" };
        foreach (var command in new[] { first, second, third, first with { Executable = " generator " } })
            await history.RememberAsync(command);
        Assert.Equal(new[] { first, third, second }, history.Entries);
    }

    [Fact]
    public async Task HistoryIsLimitedToMostRecentTwentyEntries()
    {
        var history = new CommandHistory();
        for (var i = 0; i < 25; i++) await history.RememberAsync(new("generator", $"--seed {i}", ""));
        Assert.Equal(20, history.Entries.Count);
        Assert.Equal("--seed 24", history.Entries[0].Arguments);
        Assert.Equal("--seed 5", history.Entries[^1].Arguments);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"FormatVersion\":2,\"Commands\":[]}")]
    [InlineData("{\"FormatVersion\":1,\"Commands\":[null]}")]
    public async Task InvalidHistoryReportsErrorWithoutChangingFile(string contents)
    {
        using var file = new TemporaryHistory();
        Directory.CreateDirectory(file.Folder);
        await File.WriteAllTextAsync(file.File, contents, TestContext.Current.CancellationToken);
        var history = new CommandHistory(file.File);
        await Assert.ThrowsAsync<InvalidDataException>(() => history.LoadAsync());
        Assert.Empty(history.Entries);
        Assert.Equal(contents, await File.ReadAllTextAsync(file.File, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteFailureKeepsHistoryInMemoryAndCleansTemporaryFile()
    {
        using var file = new TemporaryHistory();
        Directory.CreateDirectory(file.File);
        var history = new CommandHistory(file.File);
        var command = new CommandRequest("generator", "--count 1", "");
        var error = await Record.ExceptionAsync(() => history.RememberAsync(command));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(command, Assert.Single(history.Entries));
        Assert.Empty(Directory.GetFiles(file.Folder, "*.tmp"));
    }

    [AvaloniaFact]
    public async Task ChoosingHistoryFillsEditableFieldsAndCancelPreservesHistory()
    {
        var history = new CommandHistory();
        var saved = new CommandRequest("generator", "--count 12", "old-folder");
        await history.RememberAsync(saved);
        var window = new MainWindow(history);
        window.Show();
        try
        {
            window.FindControl<Button>("StartCommandButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.IsType<CommandDialog>(Assert.Single(window.OwnedWindows));
            Find<ComboBox>(dialog, "CommandHistoryBox").SelectedIndex = 0;
            var executable = Find<TextBox>(dialog, "ExecutableBox");
            var arguments = Find<TextBox>(dialog, "ArgumentsBox");
            var directory = Find<TextBox>(dialog, "DirectoryBox");
            Assert.Equal(saved.Executable, executable.Text);
            Assert.Equal(saved.Arguments, arguments.Text);
            Assert.Equal(saved.Directory, directory.Text);
            Assert.False(executable.IsReadOnly);
            Assert.False(arguments.IsReadOnly);
            executable.Text = "changed";
            arguments.Text = "--count 99";
            Find<Button>(dialog, "CancelCommandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(saved, Assert.Single(history.Entries));
            Assert.False(((MainWindowViewModel)window.DataContext!).Live.HasSession);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ConfirmingEditedHistoryRemembersRequestAndOffersItNextTime()
    {
        var history = new CommandHistory();
        var saved = new CommandRequest("missing-log-generator-" + Guid.NewGuid().ToString("N"), "--count 12", "");
        await history.RememberAsync(saved);
        var window = new MainWindow(history);
        window.Show();
        try
        {
            var start = window.FindControl<Button>("StartCommandButton")!;
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.IsType<CommandDialog>(Assert.Single(window.OwnedWindows));
            Find<ComboBox>(dialog, "CommandHistoryBox").SelectedIndex = 0;
            Find<TextBox>(dialog, "ArgumentsBox").Text = "--count 99";
            Find<Button>(dialog, "RunCommandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var live = ((MainWindowViewModel)window.DataContext!).Live;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (!live.HasSession) await Task.Delay(10, timeout.Token);
            await live.Completion.WaitAsync(timeout.Token);
            Assert.Equal(new[] { saved with { Arguments = "--count 99" }, saved }, history.Entries);
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.IsType<CommandDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal(history.Entries, Find<ComboBox>(dialog, "CommandHistoryBox").Items.OfType<CommandRequest>());
            Find<Button>(dialog, "CancelCommandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { window.Close(); }
    }
}
