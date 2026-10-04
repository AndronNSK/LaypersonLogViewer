using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    private readonly DispatcherTimer _streamTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _streamRefreshing, _streamWindowClosed;
    private void InitializeStreaming()
    {
        LogLines.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual source && source.GetSelfAndVisualAncestors()
                .OfType<Control>().Any(control => control.DataContext is LogLineRow))
                ViewModel.Live.FollowLatest = false;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        LogLines.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            // Ignore textbox scrolling and layout changes caused by incoming batches.
            if (_streamRefreshing || ViewModel.IsBusy || e.OffsetDelta.Y == 0
                || e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0
                || e.Source is not ScrollViewer scroll || !ReferenceEquals(scroll.TemplatedParent, LogLines)) return;
            ViewModel.Live.FollowLatest = AtLogBottom(scroll);
        });
        LogLines.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (e.Delta.Y == 0) return;
            if (e.Delta.Y > 0) ViewModel.Live.FollowLatest = false;
            // A wheel-down at the bottom has no offset change, but should resume following.
            else Dispatcher.UIThread.Post(() =>
            {
                var scroll = LogLines.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => ReferenceEquals(s.TemplatedParent, LogLines));
                if (scroll is not null && AtLogBottom(scroll)) ViewModel.Live.FollowLatest = true;
            }, DispatcherPriority.Background);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        ViewModel.Live.PropertyChanged += (_, _) =>
        {
            if (!ViewModel.Live.FollowLatest || ViewModel.Live.IsPaused || _streamRefreshing) return;
            var scroll = LogLines.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(s => ReferenceEquals(s.TemplatedParent, LogLines));
            if (scroll is not null) scroll.Offset = new Vector(scroll.Offset.X, scroll.ScrollBarMaximum.Y);
        };
        _streamTimer.Tick += async (_, _) =>
        {
            if (_streamRefreshing || _transferringPatterns || !IsEnabled || ViewModel.IsBusy || OwnedWindows.Count > 0) return;
            _streamRefreshing = true;
            try
            {
                var scroll = LogLines.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                var offset = scroll?.Offset ?? default;
                var selections = LogLines.GetVisualDescendants().OfType<TextBox>()
                    .Where(t => t.DataContext is LogLineRow && t.SelectionStart != t.SelectionEnd)
                    .Select(t => (((LogLineRow)t.DataContext!).Line, t.SelectionStart, t.SelectionEnd)).ToArray();
                if (await ViewModel.Live.RefreshAsync(ViewModel) && !_streamWindowClosed)
                {
                    LogLines.UpdateLayout();
                    foreach (var text in LogLines.GetVisualDescendants().OfType<TextBox>())
                        foreach (var (line, start, end) in selections)
                            if (text.DataContext is LogLineRow row && ReferenceEquals(row.Line, line))
                            { text.SelectionStart = start; text.SelectionEnd = end; }
                    if (scroll is not null)
                        scroll.Offset = new Vector(offset.X, ViewModel.Live.FollowLatest ? scroll.ScrollBarMaximum.Y : offset.Y);
                }
            }
            finally { _streamRefreshing = false; }
        };
        Opened += (_, _) => _streamTimer.Start();
        Closed += (_, _) => { _streamWindowClosed = true; _streamTimer.Stop(); };
    }

    private static bool AtLogBottom(ScrollViewer scroll) => scroll.Offset.Y >= scroll.ScrollBarMaximum.Y - 1;

    public void StartStandardInput()
    {
        if (!Console.IsInputRedirected)
        {
            ViewModel.ReportError("Для --stdin передайте вывод команды через |.");
            return;
        }
        ViewModel.Live.Start("Стандартный ввод", async (receive, token) =>
        {
            using var input = Console.OpenStandardInput();
            await LiveLogSources.ReadAsync(input, receive, token);
            return "Поток завершён";
        });
    }

    private async void FollowFile_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        _streamTimer.Stop();
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Следить за файлом (UTF-8)", AllowMultiple = false });
            if (files.Count == 0) return;
            using var file = files[0];
            if (file.TryGetLocalPath() is not { } path)
            { ViewModel.ReportError("Наблюдение доступно только для локального файла."); return; }
            ViewModel.Live.Start(path, async (receive, token) =>
            {
                await LiveLogSources.FollowFileAsync(path, receive, token);
                return "Остановлено";
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { ViewModel.ReportError(exception.Message); }
        finally { if (!_streamWindowClosed) _streamTimer.Start(); }
    }

    private async void StartCommand_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        _streamTimer.Stop();
        try
        {
            var command = await new CommandDialog().ShowDialog<CommandRequest?>(this);
            if (command is null) return;
            ViewModel.Live.Start(command.Executable + " " + command.Arguments, async (receive, token) =>
            {
                var code = await LiveLogSources.RunCommandAsync(command.Executable, command.Arguments, command.Directory, receive, token);
                return $"Процесс завершён · Код: {code}";
            });
        }
        finally { if (!_streamWindowClosed) _streamTimer.Start(); }
    }

    private void StopStream_Click(object? sender, RoutedEventArgs e) => ViewModel.Live.Stop();

    private async void SaveLog_Click(object? sender, RoutedEventArgs e)
    {
        var snapshot = ViewModel.Live.HasSession ? ViewModel.Live.Snapshot() : ViewModel.AllLines.ToArray();
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить журнал (все строки в памяти)", SuggestedFileName = "captured.log",
                DefaultExtension = "log", ShowOverwritePrompt = true
            });
            if (file is null) return;
            var contents = Encoding.UTF8.GetBytes(string.Concat(snapshot.Select(line => line.Text + "\n")));
            if (file.TryGetLocalPath() is { } path) await PatternFiles.WriteFileAsync(path, contents);
            else
            {
                await using var stream = await file.OpenWriteAsync();
                await stream.WriteAsync(contents);
                if (stream.CanSeek) stream.SetLength(stream.Position);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { ViewModel.ReportError($"Не удалось сохранить журнал: {exception.Message}"); }
    }
}
