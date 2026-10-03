using System.Collections.ObjectModel;
using System.ComponentModel;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<LogFilter> _filters = new();
    private IReadOnlyList<LogLine> _allLines = Array.Empty<LogLine>();
    private IReadOnlyList<LogLine> _visibleLines = Array.Empty<LogLine>();
    private IReadOnlyList<LogLineRow> _allRows = Array.Empty<LogLineRow>();
    private IReadOnlyList<LogLineRow> _matchingRows = Array.Empty<LogLineRow>();
    private bool _showFilteredOut;
    private string? _fileName;
    private string? _error;
    private bool _isBusy;
    private TimestampPattern? _timestampPattern;
    private int _entryCount;
    private int _matchingEntryCount;
    private int _timestampStartCount;

    public MainWindowViewModel() => Filters = new ReadOnlyObservableCollection<LogFilter>(_filters);

    public ReadOnlyObservableCollection<LogFilter> Filters { get; }
    public IReadOnlyList<LogLine> VisibleLines => _visibleLines;
    public IReadOnlyList<LogLineRow> DisplayLines => _showFilteredOut ? _allRows : _matchingRows;
    public bool ShowFilteredOut
    {
        get => _showFilteredOut;
        set
        {
            if (_showFilteredOut == value) return;
            _showFilteredOut = value;
            NotifyAll();
        }
    }
    public string FileName => _fileName ?? "Файл не открыт";
    public bool IsBusy => _isBusy;
    public string? Error => _error;
    public bool HasError => _error is not null;
    public bool HasNoFilters => _filters.Count == 0;
    public bool HasTimestampPattern => _timestampPattern is not null;
    public TimestampPattern? TimestampPattern => _timestampPattern;
    public int EntryCount => _entryCount;
    public int MatchingEntryCount => _matchingEntryCount;
    public int TimestampStartCount => _timestampStartCount;
    public string GroupingDescription => _timestampPattern is null
        ? "Построчно · Выделите время в строке → правая кнопка → «Начало записи по времени...»"
        : $"Начало записи: {_timestampPattern.Shape} · Позиция: {_timestampPattern.StartIndex + 1} · Начал найдено: {_timestampStartCount}";
    public string GroupingWarning => HasTimestampPattern && _allLines.Count > 0 && _timestampStartCount == 0
        ? "Шаблон не найден: весь файл считается одной записью. Выберите другой образец или сбросьте шаблон." : "";
    public bool HasGroupingWarning => GroupingWarning.Length > 0;
    public string FilterExplanation => HasTimestampPattern
        ? "Фильтры действуют на всю запись. Любое исключающее совпадение скрывает запись целиком."
        : "Показать: любое совпадение. Скрыть: исключить при любом совпадении.";
    public bool IsEmpty => DisplayLines.Count == 0;
    public string EmptyMessage => _fileName is null ? "Откройте файл журнала, чтобы начать"
        : _allLines.Count == 0 ? "Файл пуст" : "Нет строк, соответствующих фильтрам";
    public string Status => _isBusy ? "Обработка…"
        : $"Показано {DisplayLines.Count:N0} из {_allLines.Count:N0} строк · Совпадений: {_visibleLines.Count:N0} · Фильтров: {_filters.Count}"
          + (HasTimestampPattern ? $" · Записей подходит: {_matchingEntryCount:N0} из {_entryCount:N0}" : "");

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
                return (All: lines, Projection: BuildProjection(lines, filters, _timestampPattern));
            });
            _allLines = result.All;
            ApplyProjection(result.Projection);
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

    public async Task SetTimestampPatternAsync(TimestampPattern? pattern)
    {
        BeginOperation();
        try
        {
            var filters = _filters.ToArray();
            var projection = await Task.Run(() => BuildProjection(_allLines, filters, pattern));
            _timestampPattern = pattern;
            ApplyProjection(projection);
        }
        finally { EndOperation(); }
    }

    public async Task<int> PreviewTimestampPatternAsync(TimestampPattern pattern)
    {
        BeginOperation();
        try { return await Task.Run(() => _allLines.Count(line => pattern.Matches(line.Text))); }
        finally { EndOperation(); }
    }

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
            var projection = await Task.Run(() => BuildProjection(_allLines, filters, _timestampPattern));
            ApplyProjection(projection);
        }
        finally { EndOperation(); }
    }

    private sealed record Projection(IReadOnlyList<LogLine> Visible, IReadOnlyList<LogLineRow> Rows,
        IReadOnlyList<LogLineRow> MatchingRows, int Entries, int MatchingEntries, int TimestampStarts);

    private static Projection BuildProjection(IReadOnlyList<LogLine> lines,
        LogFilter[] filters, TimestampPattern? pattern)
    {
        // Avoid allocating one entry object per line when grouping is disabled.
        if (pattern is null)
        {
            var lineMatches = LogFilterEngine.Apply(lines, filters);
            var numbers = lineMatches.Select(line => line.Number).ToHashSet();
            var lineRows = lines.Select(line => new LogLineRow(line, !numbers.Contains(line.Number))).ToArray();
            return new Projection(lineMatches, lineRows, lineRows.Where(row => !row.IsFilteredOut).ToArray(),
                lines.Count, lineMatches.Count, 0);
        }
        var entries = LogEntryParser.Split(lines, pattern);
        var matching = LogFilterEngine.ApplyEntries(entries, filters);
        var matchingStarts = matching.Select(entry => entry.Lines[0].Number).ToHashSet();
        var rows = new List<LogLineRow>(lines.Count);
        var timestampStarts = 0;
        foreach (var entry in entries)
        {
            var first = entry.Lines[0];
            if (pattern?.Matches(first.Text) == true) timestampStarts++;
            var excluded = !matchingStarts.Contains(first.Number);
            foreach (var line in entry.Lines)
                rows.Add(new LogLineRow(line, excluded, pattern is not null && line.Number == first.Number));
        }
        var matchingRows = rows.Where(row => !row.IsFilteredOut).ToArray();
        return new Projection(matchingRows.Select(row => row.Line).ToArray(), rows, matchingRows,
            entries.Count, matching.Count, timestampStarts);
    }

    private void ApplyProjection(Projection projection)
    {
        _visibleLines = projection.Visible;
        _allRows = projection.Rows;
        _matchingRows = projection.MatchingRows;
        _entryCount = projection.Entries;
        _matchingEntryCount = projection.MatchingEntries;
        _timestampStartCount = projection.TimestampStarts;
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
