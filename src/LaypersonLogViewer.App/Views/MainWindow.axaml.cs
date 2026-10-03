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

    public MainWindow() : this(null) { }

    public MainWindow(IStatisticsSettingsStore? statisticsStore)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(statisticsStore);
        Closing += async (_, e) =>
        {
            if (ViewModel.Statistics.PendingSave.IsCompleted) return;
            e.Cancel = true;
            await ViewModel.Statistics.PendingSave;
            Close();
        };
        Closed += (_, _) => ViewModel.Statistics.Dispose();
    }

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    private async void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        try
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
            if (files.Count == 0) return;
            using var file = files[0];
            // The storage provider's default sharing can conflict with an active logger.
            // Use explicit sharing for local files; retain provider access for virtual files.
            var localPath = file.TryGetLocalPath();
            await using var stream = localPath is not null
                ? LogFileReader.OpenRead(localPath)
                : await file.OpenReadAsync();
            await ViewModel.LoadAsync(file.Name, stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ViewModel.ReportError($"Не удалось открыть файл: {exception.Message}");
        }
    }

    private async void Include_Click(object? sender, RoutedEventArgs e) => await AddFilterAsync(FilterKind.Include);
    private async void Exclude_Click(object? sender, RoutedEventArgs e) => await AddFilterAsync(FilterKind.Exclude);

    private async Task AddFilterAsync(FilterKind kind, string? initialText = null, LogFilter? parent = null)
    {
        if (ViewModel.IsBusy) return;
        initialText ??= LogLines.GetVisualDescendants().OfType<TextBox>()
            .OrderByDescending(text => ReferenceEquals(text, _activeLogText))
            .FirstOrDefault(text => text.SelectionStart != text.SelectionEnd)?.SelectedText ?? "";
        var filter = await new FilterDialog(kind, initialText, addingCondition: parent is not null).ShowDialog<LogFilter?>(this);
        if (filter is null) return;

        // TextBox keeps selection across focus changes; only the row rebuild needs help.
        var selections = new Dictionary<LogLine, (int Start, int End)>(ReferenceEqualityComparer.Instance);
        foreach (var text in LogLines.GetVisualDescendants().OfType<TextBox>())
            if (text.DataContext is LogLineRow row && text.SelectionStart != text.SelectionEnd)
                selections[row.Line] = (text.SelectionStart, text.SelectionEnd);

        if (parent is null) await ViewModel.AddFilterAsync(filter);
        else await ViewModel.AddConditionAsync(parent, filter.Condition);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogLines.UpdateLayout();
            foreach (var text in LogLines.GetVisualDescendants().OfType<TextBox>())
                if (text.DataContext is LogLineRow row && selections.TryGetValue(row.Line, out var selection))
                {
                    text.SelectionStart = selection.Start;
                    text.SelectionEnd = selection.End;
                }
        }, DispatcherPriority.Loaded);
    }

    private void LogText_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TextBox text) return;
        e.Handled = true;

        // Keep the exact selection before opening a popup or dialog changes focus.
        var selectedText = text.SelectedText;
        var canAdd = !ViewModel.IsBusy && !string.IsNullOrWhiteSpace(selectedText);
        var include = new MenuItem { Header = "Показать строки…", IsEnabled = canAdd };
        var exclude = new MenuItem { Header = "Скрыть строки…", IsEnabled = canAdd };
        var selectionStart = Math.Min(text.SelectionStart, text.SelectionEnd);
        var canSetTimestamp = canAdd && selectedText.Any(char.IsAsciiDigit);
        var timestamp = new MenuItem { Header = "Начало записи по времени…", IsEnabled = canSetTimestamp };
        var statistics = new MenuItem { Header = "Создать статистику…", IsEnabled = ViewModel.Statistics.IsReady };
        var example = text.Text ?? "";
        var menu = new ContextMenu
        {
            ItemsSource = new[] { include, exclude, timestamp, statistics },
            Placement = e.TryGetPosition(text, out _) ? PlacementMode.Pointer : PlacementMode.Bottom
        };
        include.Click += async (_, _) =>
        {
            menu.Close();
            if (canAdd) await AddFilterAsync(FilterKind.Include, selectedText);
        };
        exclude.Click += async (_, _) =>
        {
            menu.Close();
            if (canAdd) await AddFilterAsync(FilterKind.Exclude, selectedText);
        };
        timestamp.Click += async (_, _) =>
        {
            menu.Close();
            if (!canSetTimestamp || ViewModel.IsBusy) return;
            var pattern = new TimestampPattern(selectedText, selectionStart);
            var count = await ViewModel.PreviewTimestampPatternAsync(pattern);
            var accepted = await new TimestampPatternDialog(pattern, count).ShowDialog<TimestampPattern?>(this);
            if (accepted is not null) await ViewModel.SetTimestampPatternAsync(accepted);
        };
        statistics.Click += async (_, _) =>
        {
            menu.Close();
            await EditStatisticsAsync(example: example);
        };
        text.ContextMenu = menu;
        menu.Open(text);
    }

    private void LogText_GotFocus(object? sender, RoutedEventArgs e) => _activeLogText = sender as TextBox;

    public async Task EditStatisticsAsync(StatisticsPattern? pattern = null, string? example = null)
    {
        await ViewModel.Statistics.Initialization;
        example ??= _activeLogText?.DataContext is LogLineRow row ? row.Text : "";
        var dialog = new StatisticsPatternDialog(example, ViewModel.Statistics.SourceLines, pattern);
        var accepted = await dialog.ShowDialog<StatisticsPattern?>(this);
        if (accepted is null) return;
        ViewModel.Statistics.SavePattern(accepted);
        LowerTabs.SelectedIndex = 1;
    }

    private void LogText_DataContextChanged(object? sender, EventArgs e)
    {
        // Virtualized rows can be reused for another line after scrolling or filtering.
        if (sender is TextBox text) text.ClearSelection();
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
