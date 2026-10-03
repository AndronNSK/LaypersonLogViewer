using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
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

    private async Task AddFilterAsync(FilterKind kind, string initialText = "")
    {
        if (ViewModel.IsBusy) return;
        var filter = await new FilterDialog(kind, initialText).ShowDialog<LogFilter?>(this);
        if (filter is not null) await ViewModel.AddFilterAsync(filter);
    }

    private void LogText_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not SelectableTextBlock text) return;
        e.Handled = true;

        // Keep the exact selection before opening a popup or dialog changes focus.
        var selectedText = text.SelectedText;
        var canAdd = !ViewModel.IsBusy && !string.IsNullOrWhiteSpace(selectedText);
        var include = new MenuItem { Header = "Показать строки...", IsEnabled = canAdd };
        var exclude = new MenuItem { Header = "Скрыть строки", IsEnabled = canAdd };
        var selectionStart = Math.Min(text.SelectionStart, text.SelectionEnd);
        var canSetTimestamp = canAdd && selectedText.Any(char.IsAsciiDigit);
        var timestamp = new MenuItem { Header = "Начало записи по времени...", IsEnabled = canSetTimestamp };
        var menu = new ContextMenu
        {
            ItemsSource = new[] { include, exclude, timestamp },
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
        text.ContextMenu = menu;
        menu.Open(text);
    }

    private void LogText_DataContextChanged(object? sender, EventArgs e)
    {
        // Virtualized rows can be reused for another line after scrolling or filtering.
        if (sender is SelectableTextBlock text) text.ClearSelection();
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy && sender is Button { DataContext: LogFilter filter })
            await ViewModel.RemoveFilterAsync(filter);
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
