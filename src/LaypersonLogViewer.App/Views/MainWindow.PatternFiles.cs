using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    private bool _transferringPatterns;
    private async void LoadFilters_Click(object? sender, RoutedEventArgs e) => await TransferPatternsAsync(false, false);
    private async void SaveFilters_Click(object? sender, RoutedEventArgs e) => await TransferPatternsAsync(false, true);

    public async Task TransferPatternsAsync(bool statistics, bool save)
    {
        if (_transferringPatterns || ViewModel.IsBusy) return;
        _transferringPatterns = true;
        var label = statistics ? "шаблоны статистики" : "фильтры";
        var type = new FilePickerFileType(statistics ? "Шаблоны статистики" : "Фильтры") { Patterns = ["*.json"] };
        try
        {
            if (save)
            {
                // Prepare a consistent document before showing the save dialog.
                using var contents = new MemoryStream();
                if (statistics) await ViewModel.Statistics.ExportAsync(contents);
                else await ViewModel.SaveFiltersAsync(contents);
                using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Сохранить {label}", DefaultExtension = "json", FileTypeChoices = [type],
                    SuggestedFileName = statistics ? "statistics.json" : "filters.json", ShowOverwritePrompt = true
                });
                if (file is null) return;
                IsEnabled = false;
                if (file.TryGetLocalPath() is { } path)
                    await PatternFiles.WriteFileAsync(path, contents.ToArray());
                else
                {
                    await using var output = await file.OpenWriteAsync();
                    contents.Position = 0;
                    await contents.CopyToAsync(output);
                    if (output.CanSeek) output.SetLength(output.Position);
                }
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Загрузить {label} (заменить текущие)", AllowMultiple = false, FileTypeFilter = [type]
                });
                if (files.Count == 0) return;
                using var file = files[0];
                IsEnabled = false;
                await using var input = await file.OpenReadAsync();
                if (statistics) await ViewModel.Statistics.ImportAsync(input);
                else await ViewModel.LoadFiltersAsync(input);
            }
            ViewModel.ReportError(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ViewModel.ReportError($"Не удалось {(save ? "сохранить" : "загрузить")} {label}: {exception.Message}");
        }
        finally { IsEnabled = true; _transferringPatterns = false; }
    }
}
