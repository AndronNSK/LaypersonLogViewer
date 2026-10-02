using Avalonia.Controls;
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

    private async Task AddFilterAsync(FilterKind kind)
    {
        if (ViewModel.IsBusy) return;
        var filter = await new FilterDialog(kind).ShowDialog<LogFilter?>(this);
        if (filter is not null) await ViewModel.AddFilterAsync(filter);
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
}
