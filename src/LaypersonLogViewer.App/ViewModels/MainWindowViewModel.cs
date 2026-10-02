using System.Collections.ObjectModel;
using System.ComponentModel;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<LogFilter> _filters = new();
    private IReadOnlyList<LogLine> _allLines = Array.Empty<LogLine>();
    private IReadOnlyList<LogLine> _visibleLines = Array.Empty<LogLine>();
    private string? _fileName;
    private string? _error;
    private bool _isBusy;

    public MainWindowViewModel() => Filters = new ReadOnlyObservableCollection<LogFilter>(_filters);

    public ReadOnlyObservableCollection<LogFilter> Filters { get; }
    public IReadOnlyList<LogLine> VisibleLines => _visibleLines;
    public string FileName => _fileName ?? "Файл не открыт";
    public bool IsBusy => _isBusy;
    public string? Error => _error;
    public bool HasError => _error is not null;
    public bool HasNoFilters => _filters.Count == 0;
    public bool IsEmpty => _visibleLines.Count == 0;
    public string EmptyMessage => _fileName is null ? "Откройте файл журнала, чтобы начать"
        : _allLines.Count == 0 ? "Файл пуст" : "Нет строк, соответствующих фильтрам";
    public string Status => _isBusy ? "Обработка…"
        : $"Показано {_visibleLines.Count:N0} из {_allLines.Count:N0} строк · Фильтров: {_filters.Count}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string fileName, Stream stream)
    {
        BeginOperation();
        try
        {
            // Read and filter away from the UI thread. Commit only after both succeed,
            // so a failed load does not discard the currently displayed file.
            var filters = _filters.ToArray();
            var result = await Task.Run(async () =>
            {
                var lines = await LogFileReader.ReadAsync(stream);
                return (All: lines, Visible: LogFilterEngine.Apply(lines, filters));
            });
            _allLines = result.All;
            _visibleLines = result.Visible;
            _fileName = fileName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or System.Text.DecoderFallbackException)
        {
            _error = $"Не удалось прочитать файл: {exception.Message}";
        }
        finally { EndOperation(); }
    }

    public Task AddFilterAsync(LogFilter filter) => ChangeFiltersAsync(() => _filters.Add(filter));
    public Task RemoveFilterAsync(LogFilter filter) => ChangeFiltersAsync(() => _filters.Remove(filter));
    public Task ClearFiltersAsync() => ChangeFiltersAsync(_filters.Clear);

    public void ReportError(string message)
    {
        _error = message;
        NotifyAll();
    }

    private async Task ChangeFiltersAsync(Action change)
    {
        BeginOperation();
        try
        {
            change();
            var filters = _filters.ToArray();
            _visibleLines = await Task.Run(() => LogFilterEngine.Apply(_allLines, filters));
        }
        finally { EndOperation(); }
    }

    private void BeginOperation()
    {
        if (_isBusy) throw new InvalidOperationException("An operation is already running.");
        _isBusy = true;
        _error = null;
        NotifyAll();
    }

    private void EndOperation()
    {
        _isBusy = false;
        NotifyAll();
    }

    // An empty property name tells bindings to refresh all properties of this small view model.
    private void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
