using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow : Window
{
    private TextBox? _activeLogText;
    private long _logTextActivation;
    private bool _openingFile;
    private GridLength _lowerPaneHeight = new(210);
    private readonly CommandHistory _commandHistory;

    public MainWindow() : this(new CommandHistory(CommandHistory.DefaultPath)) { }

    public MainWindow(CommandHistory commandHistory)
    {
        _commandHistory = commandHistory;
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        UpdateMenuLayout();
        InitializeStreaming();
        InitializeLogTextSelection();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainWindowViewModel.FormatJson)) return;
            ClearLogTextRange();
            foreach (var text in RealizedLogTextBoxes()) ClearLogTextSelection(text);
        };
        AddHandler(KeyDownEvent, Search_KeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, File_DragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, File_Drop, RoutingStrategies.Bubble, handledEventsToo: true);
        Closed += (_, _) =>
        {
            ViewModel.Live.Dispose(); ViewModel.Statistics.Dispose(); ViewModel.Search.Dispose();
            _updateTimer.Stop(); _updateCancellation.Cancel(); _updateClient.Dispose();
        };
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private TextBox? ActiveLogText => _activeLogText?.IsAttachedToVisualTree() == true ? _activeLogText : null;

    private void LowerPaneToggle_Click(object? sender, RoutedEventArgs e) => SetLowerPaneVisible(!LowerPane.IsVisible);

    private void SetLowerPaneVisible(bool visible)
    {
        if (visible == LowerPane.IsVisible) return;
        var row = MainLayout.RowDefinitions[3];
        if (!visible) _lowerPaneHeight = row.Height;
        row.MinHeight = visible ? 150 : 0;
        row.Height = visible ? _lowerPaneHeight : new GridLength(0);
        LowerPane.IsVisible = visible;
        LowerPaneDivider.IsEnabled = visible;
        LowerPaneToggle.Classes.Set("collapsed", !visible);
        ToolTip.SetTip(LowerPaneToggle, visible ? "Скрыть панель вкладок" : "Показать панель вкладок");
    }

    private bool CanOpenFile => !_openingFile && !ViewModel.IsBusy && !_transferringPatterns
        && IsEnabled && OwnedWindows.Count == 0 && !_streamWindowClosed;

    private void File_DragOver(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = CanOpenFile && files is { Length: 1 } && files[0] is IStorageFile
            ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void File_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (!CanOpenFile || e.DataTransfer.TryGetFiles() is not { Length: 1 } files
            || files[0] is not IStorageFile file) return;
        e.DragEffects = DragDropEffects.Copy;
        await OpenFileAsync(() => Task.FromResult<IStorageFile?>(file));
    }

    private async void OpenFile_Click(object? sender, RoutedEventArgs e) => await OpenFileAsync(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть файл журнала",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Файлы журналов") { Patterns = new[] { "*.log", "*.txt", "*.jsonl" } },
                FilePickerFileTypes.All
            }
        });
        return files.Count == 0 ? null : files[0];
    });

    private async Task OpenFileAsync(Func<Task<IStorageFile?>> selectFile)
    {
        if (!CanOpenFile) return;
        _openingFile = true;
        _streamTimer.Stop();
        try
        {
            using var file = await selectFile();
            if (file is null || _streamWindowClosed) return;
            if (file.TryGetLocalPath() is not { } path)
            {
                ViewModel.ReportError("Наблюдение доступно только для локального файла.");
                return;
            }
            // Check access before replacing the current source, allowing active writers.
            using (LogFileReader.OpenRead(path)) { }
            ViewModel.ReportError(null);
            ViewModel.Live.Start(path, async (receive, token) =>
            {
                await LiveLogSources.FollowFileAsync(path, receive, token);
                return "Остановлено";
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            ViewModel.ReportError($"Не удалось открыть файл: {exception.Message}");
        }
        finally
        {
            _openingFile = false;
            if (!_streamWindowClosed) _streamTimer.Start();
        }
    }

    public async Task StartFromArgumentsAsync(IReadOnlyList<string> args)
    {
        StartupOptions options;
        try { options = StartupOptions.Parse(args); }
        catch (ArgumentException error) { ViewModel.ReportError(error.Message); return; }
        if (options.StandardInput) StartStandardInput();
        else if (options.FilePath is { } path)
        {
            await OpenFileAsync(async () => await StorageProvider.TryGetFileFromPathAsync(Path.GetFullPath(path))
                ?? throw new FileNotFoundException("Файл не найден.", path));
        }
    }

    private async void Include_Click(object? sender, RoutedEventArgs e) => await AddFilterAsync(FilterKind.Include);
    private async void Exclude_Click(object? sender, RoutedEventArgs e) => await AddFilterAsync(FilterKind.Exclude);

    private async Task AddFilterAsync(FilterKind kind, string? initialText = null, LogFilter? parent = null)
    {
        if (ViewModel.IsBusy) return;
        initialText ??= SingleLineSelection ?? "";
        var filter = await new FilterDialog(kind, initialText, addingCondition: parent is not null).ShowDialog<LogFilter?>(this);
        if (filter is null) return;
        await ApplyFilterAsync(filter, parent);
    }

    private async Task ApplyFilterAsync(LogFilter filter, LogFilter? parent = null)
    {
        if (ViewModel.IsBusy) return;
        var active = _logTextRange is null ? ActiveLogText : null;
        var selectedRow = active?.DataContext as LogLineRow;
        var start = active?.SelectionStart ?? 0;
        var end = active?.SelectionEnd ?? 0;
        var activation = _logTextActivation;
        var previousDisplay = ViewModel.DisplayLines;

        if (parent is null) await ViewModel.AddFilterAsync(filter);
        else await ViewModel.AddConditionAsync(parent, filter.Condition);
        if (selectedRow is null || start == end) return;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (activation != _logTextActivation) return;
            if (!ReferenceEquals(previousDisplay, ViewModel.DisplayLines))
            {
                var index = LogSearch.IndexOfLine(DisplayedLogLines, selectedRow.Line);
                if (index >= 0) LogLines.ScrollIntoView(index);
            }
            LogLines.UpdateLayout();
            // Restore the range only if layout recycled its text box and the user
            // has not moved to another log line in the meantime.
            if (activation != _logTextActivation || ActiveLogText is not null) return;
            var text = LogLines.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(box => ReferenceEquals(box.DataContext, selectedRow));
            if (text is null) return;
            ActivateLogText(text);
            text.SelectionStart = start;
            text.SelectionEnd = end;
        }, DispatcherPriority.Loaded);
    }

    private void LogText_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TextBox text) return;
        e.Handled = true;
        var inRange = text.DataContext is LogLineRow row && IsInLogTextRange(row);
        if (!inRange)
        {
            ClearLogTextRange();
            ActivateLogText(text);
        }
        else RefreshLogTextRange();

        // Keep the exact selection before opening a popup or dialog changes focus.
        var selectedText = inRange ? "" : text.SelectedText;
        var canAdd = !ViewModel.IsBusy && !string.IsNullOrWhiteSpace(selectedText);
        var include = new MenuItem { Header = "Показать строки", IsEnabled = canAdd };
        var exclude = new MenuItem { Header = "Скрыть строки", IsEnabled = canAdd };
        var selectionStart = Math.Min(text.SelectionStart, text.SelectionEnd);
        var original = (text.DataContext as LogLineRow)?.Text ?? "";
        var canSetTimestamp = canAdd && selectionStart + selectedText.Length <= original.Length
            && original.AsSpan(selectionStart, selectedText.Length).SequenceEqual(selectedText)
            && selectedText.Any(char.IsAsciiDigit)
            && !selectedText.Contains('\r') && !selectedText.Contains('\n');
        var timestamp = new MenuItem { Header = "Начало записи по времени", IsEnabled = canSetTimestamp };
        var statistics = new MenuItem { Header = "Создать статистику…" };
        var copy = new MenuItem { Header = "Копировать", IsEnabled = inRange || selectedText.Length > 0 };
        var example = (text.DataContext as LogLineRow)?.Text ?? "";
        var menu = new ContextMenu
        {
            ItemsSource = new[] { timestamp, include, exclude, statistics, copy },
            Placement = e.TryGetPosition(text, out _) ? PlacementMode.Pointer : PlacementMode.Bottom
        };
        include.Click += async (_, _) =>
        {
            menu.Close();
            if (canAdd) await ApplyFilterAsync(new LogFilter(FilterKind.Include, selectedText));
        };
        exclude.Click += async (_, _) =>
        {
            menu.Close();
            if (canAdd) await ApplyFilterAsync(new LogFilter(FilterKind.Exclude, selectedText));
        };
        timestamp.Click += async (_, _) =>
        {
            menu.Close();
            if (!canSetTimestamp || ViewModel.IsBusy) return;
            var pattern = new TimestampPattern(selectedText, selectionStart);
            await ViewModel.SetTimestampPatternAsync(pattern);
            SetLowerPaneVisible(true);
            LowerTabs.SelectedItem = TimestampTab;
        };
        statistics.Click += async (_, _) =>
        {
            menu.Close();
            await EditStatisticsAsync(example: example);
        };
        copy.Click += async (_, _) =>
        {
            menu.Close();
            await CopyLogTextAsync(inRange ? GetLogTextRange() : selectedText);
        };
        text.ContextMenu = menu;
        menu.Open(text);
    }

    private void LogText_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (_logTextRange is null) ActivateLogText(sender as TextBox);
    }

    private void ActivateLogText(TextBox? text)
    {
        if (ReferenceEquals(_activeLogText, text)) return;
        if (_activeLogText is { } previous) ClearLogTextSelection(previous);
        _activeLogText = text;
        _logTextActivation++;
    }

    private static void ClearLogTextSelection(TextBox text)
    {
        if (text.SelectionStart == text.SelectionEnd) return;
        // Moving the old caret can scroll the log away from the line being clicked.
        var caret = text.CaretIndex;
        text.SelectionStart = caret;
        text.SelectionEnd = caret;
    }

    public async Task EditStatisticsAsync(StatisticsPattern? pattern = null, string? example = null)
    {
        example ??= ActiveLogText?.DataContext is LogLineRow row ? row.Text : "";
        var dialog = new StatisticsPatternDialog(example, ViewModel.Statistics.SourceLines, pattern);
        var accepted = await dialog.ShowDialog<StatisticsPattern?>(this);
        if (accepted is null) return;
        ViewModel.Statistics.SavePattern(accepted);
        LowerTabs.SelectedIndex = 1;
    }

    private void LogText_DataContextChanged(object? sender, EventArgs e)
    {
        // Virtualized rows can be reused for another line after scrolling or filtering.
        if (sender is not TextBox text) return;
        ClearLogTextSelection(text);
        if (ReferenceEquals(_activeLogText, text)) _activeLogText = null;
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy && sender is Button { DataContext: LogFilter filter })
            await ViewModel.RemoveFilterAsync(filter);
    }

    private async void AddCondition_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LogFilter parent })
            await AddFilterAsync(parent.Kind, parent: parent);
    }

    private async void RemoveCondition_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || sender is not Button { DataContext: LogFilterCondition condition } button) return;
        var parent = button.GetVisualAncestors().OfType<Control>()
            .Select(control => control.DataContext).OfType<LogFilter>().FirstOrDefault();
        if (parent is not null) await ViewModel.RemoveConditionAsync(parent, condition);
    }

    private async void Clear_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy) await ViewModel.ClearFiltersAsync();
    }

    private async void ResetTimestamp_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy) await ViewModel.SetTimestampPatternAsync(null);
    }
}
