using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Collections;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<LogFilter> _filters = new();
    private AvaloniaList<LogLineRow> _displayRows = new();
    private IReadOnlyList<LogLine> _allLines = Array.Empty<LogLine>();
    private IReadOnlyList<LogLine> _visibleLines = Array.Empty<LogLine>();
    private LogLineRow[] _allRows = [];
    private IReadOnlyList<LogLineRow> _matchingRows = Array.Empty<LogLineRow>();
    private bool _showFilteredOut;
    private bool _wordWrap;
    private bool _formatJson;
    public bool FormatJson
    {
        get => _formatJson;
        set
        {
            if (_formatJson == value) return;
            _formatJson = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormatJson)));
        }
    }
    private string? _fileName;
    private string? _error;
    private bool _isBusy;
    private long _projectionRevision;
    private TimestampPattern? _timestampPattern;
    private int _entryCount;
    private int _matchingEntryCount;
    private int _timestampStartCount;

    public MainWindowViewModel()
    {
        Filters = new ReadOnlyObservableCollection<LogFilter>(_filters);
        Statistics = new StatisticsViewModel();
        Live.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(Live.Status))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        };
    }

    public StatisticsViewModel Statistics { get; }
    public LiveStreamViewModel Live { get; } = new();
    public LogSearchViewModel Search { get; } = new();
    public IReadOnlyList<LogLine> AllLines => _allLines;

    public async Task<bool> ApplyLiveLinesAsync(string sourceName, IReadOnlyList<LogLine> lines,
        Func<bool>? isCurrent = null)
    {
        if (_isBusy) return false;
        var revision = ++_projectionRevision;
        var filters = _filters.ToArray();
        var pattern = _timestampPattern;
        var previousRows = _allRows;
        var projection = await Task.Run(() => BuildProjection(lines, CreateRows(lines, previousRows), filters, pattern));
        // User operations and newer snapshots take precedence over this batch.
        if (_isBusy || revision != _projectionRevision || isCurrent?.Invoke() == false) return false;
        _allLines = lines;
        _fileName = sourceName;
        ApplyProjection(projection, liveUpdate: true);
        foreach (var property in new[] { nameof(AllLines), nameof(VisibleLines), nameof(FileName),
            nameof(EntryCount), nameof(MatchingEntryCount), nameof(TimestampStartCount),
            nameof(GroupingDescription), nameof(TimestampEntrySummary), nameof(GroupingWarning), nameof(HasGroupingWarning),
            nameof(IsEmpty), nameof(EmptyMessage), nameof(Status) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        return true;
    }

    public ReadOnlyObservableCollection<LogFilter> Filters { get; }
    public IReadOnlyList<LogLine> VisibleLines => _visibleLines;
    public IReadOnlyList<LogLineRow> DisplayLines => _displayRows;
    public bool WordWrap
    {
        get => _wordWrap;
        set
        {
            if (_wordWrap == value) return;
            _wordWrap = value;
            foreach (var property in new[] { nameof(WordWrap), nameof(LogTextWrapping), nameof(LogHorizontalScrollBarVisibility) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
    public Avalonia.Media.TextWrapping LogTextWrapping => WordWrap
        ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;
    public Avalonia.Controls.Primitives.ScrollBarVisibility LogHorizontalScrollBarVisibility => WordWrap
        ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
    public bool ShowFilteredOut
    {
        get => _showFilteredOut;
        set
        {
            if (_showFilteredOut == value) return;
            _showFilteredOut = value;
            UpdateDisplayRows();
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
        ? "Построчно · Выделите время в строке → правая кнопка → «Начало записи по времени»"
        : $"Начало записи: {_timestampPattern.Shape} · Позиция: {_timestampPattern.StartIndex + 1} · Начал найдено: {_timestampStartCount}";
    public string GroupingWarning => HasTimestampPattern && _allLines.Count > 0 && _timestampStartCount == 0
        ? "Шаблон не найден: весь файл считается одной записью. Выберите другой образец или сбросьте шаблон." : "";
    public bool HasGroupingWarning => GroupingWarning.Length > 0;
    public string TimestampEntrySummary => HasTimestampPattern
        ? $"Записей подходит: {_matchingEntryCount:N0} из {_entryCount:N0}" : "";
    public string FilterExplanation => HasTimestampPattern
        ? "В группе — все условия в одной записи. Между группами — ИЛИ. «Скрыть» имеет приоритет."
        : "В группе — все условия в одной строке. Между группами — ИЛИ. «Скрыть» имеет приоритет.";
    public bool IsEmpty => DisplayLines.Count == 0;
    public string EmptyMessage => _fileName is null ? "Откройте файл журнала, чтобы начать"
        : _allLines.Count == 0 ? "Файл пуст" : "Нет строк, соответствующих фильтрам";
    public string Status => (_isBusy ? "Обработка…"
        : $"Показано {DisplayLines.Count:N0} из {_allLines.Count:N0} строк · Совпадений: {_visibleLines.Count:N0} · Фильтров: {_filters.Count}")
          + (Live.HasSession && Live.Status.Length > 0 ? " · " + Live.Status : "");

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync(string fileName, Stream stream)
    {
        BeginOperation();
        try
        {
            // Read and filter away from the UI thread. Commit only after both succeed,
            // so a failed load does not discard the currently displayed file.
            var filters = _filters.ToArray();
            var pattern = _timestampPattern;
            var result = await Task.Run(async () =>
            {
                var lines = await LogFileReader.ReadAsync(stream);
                return (All: lines, Projection: BuildProjection(lines, CreateRows(lines, []), filters, pattern));
            });
            _allLines = result.All;
            _fileName = fileName;
            ApplyProjection(result.Projection);
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

    public Task SaveFiltersAsync(Stream stream) => PatternFiles.SaveFiltersAsync(stream, _filters.ToArray());

    public async Task LoadFiltersAsync(Stream stream)
    {
        // Validate the whole file before replacing the current groups.
        var filters = await PatternFiles.LoadFiltersAsync(stream);
        await ChangeFiltersAsync(() =>
        {
            _filters.Clear();
            foreach (var filter in filters) _filters.Add(filter);
        });
    }

    public Task AddConditionAsync(LogFilter parent, LogFilterCondition condition) => ChangeFiltersAsync(() =>
    {
        var index = _filters.IndexOf(parent);
        if (index >= 0)
            _filters[index] = new LogFilter(parent.Kind, parent.Condition, parent.AdditionalConditions.Append(condition));
    });

    public Task RemoveConditionAsync(LogFilter parent, LogFilterCondition condition) => ChangeFiltersAsync(() =>
    {
        var index = _filters.IndexOf(parent);
        if (index >= 0)
            _filters[index] = new LogFilter(parent.Kind, parent.Condition,
                parent.AdditionalConditions.Where(child => !ReferenceEquals(child, condition)));
    });

    public async Task SetTimestampPatternAsync(TimestampPattern? pattern)
    {
        BeginOperation();
        try
        {
            var filters = _filters.ToArray();
            var projection = await Task.Run(() => BuildProjection(_allLines, _allRows, filters, pattern));
            _timestampPattern = pattern;
            ApplyProjection(projection);
        }
        finally { EndOperation(); }
    }

    public void ReportError(string? message)
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
            var projection = await Task.Run(() => BuildProjection(_allLines, _allRows, filters, _timestampPattern));
            ApplyProjection(projection);
        }
        finally { EndOperation(); }
    }

    [Flags]
    private enum RowState : byte { FilteredOut = 1, EntryStart = 2 }

    private sealed record Projection(IReadOnlyList<LogLine> Visible, LogLineRow[] Rows, RowState[]? States,
        IReadOnlyList<LogLineRow> MatchingRows, int Entries, int MatchingEntries, int TimestampStarts);

    private static LogLineRow[] CreateRows(IReadOnlyList<LogLine> lines, LogLineRow[] previous)
    {
        var rows = new LogLineRow[lines.Count];
        var oldIndex = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            var line = lines[index];
            while (oldIndex < previous.Length && previous[oldIndex].Number < line.Number) oldIndex++;
            rows[index] = oldIndex < previous.Length && ReferenceEquals(previous[oldIndex].Line, line)
                ? previous[oldIndex++] : new LogLineRow(line, false);
        }
        return rows;
    }

    private static Projection BuildProjection(IReadOnlyList<LogLine> lines, LogLineRow[] rows,
        LogFilter[] filters, TimestampPattern? pattern)
    {
        if (pattern is null && filters.Length == 0)
            return new Projection(lines, rows, null, rows, lines.Count, lines.Count, 0);

        var states = new RowState[rows.Length];
        var visible = filters.Length > 0 ? new List<LogLine>() : null;
        var matchingRows = filters.Length > 0 ? new List<LogLineRow>() : null;
        var entries = 0;
        var matchingEntries = 0;
        var timestampStarts = 0;
        foreach (var (start, end, matches, hasTimestamp) in LogFilterEngine.Evaluate(lines, filters, pattern))
        {
            entries++;
            if (hasTimestamp) timestampStarts++;
            if (!matches) states.AsSpan(start, end - start).Fill(RowState.FilteredOut);
            else
            {
                matchingEntries++;
                if (visible is not null && matchingRows is not null)
                {
                    for (var index = start; index < end; index++)
                    {
                        visible.Add(lines[index]);
                        matchingRows.Add(rows[index]);
                    }
                }
            }
            if (pattern is not null) states[start] |= RowState.EntryStart;
        }
        return new Projection(visible ?? lines, rows, states, matchingRows ?? (IReadOnlyList<LogLineRow>)rows,
            entries, matchingEntries, timestampStarts);
    }

    private void ApplyProjection(Projection projection, bool liveUpdate = false)
    {
        _visibleLines = projection.Visible;
        _allRows = projection.Rows;
        for (var index = 0; index < _allRows.Length; index++)
        {
            var state = projection.States?[index] ?? 0;
            _allRows[index].UpdateDisplayState((state & RowState.FilteredOut) != 0, (state & RowState.EntryStart) != 0);
        }
        _matchingRows = projection.MatchingRows;
        UpdateDisplayRows();
        _entryCount = projection.Entries;
        _matchingEntryCount = projection.MatchingEntries;
        _timestampStartCount = projection.TimestampStarts;
        Statistics.SetSources(_allLines, _visibleLines, _fileName is not null, liveUpdate);
    }

    private void UpdateDisplayRows()
    {
        var desired = _showFilteredOut ? _allRows : _matchingRows;
        Search.SetLines(_showFilteredOut ? _allLines : _visibleLines);
        if (UpdateDisplayedRanges(desired)) return;

        // Many small edits would repeatedly shift a large list and invalidate Avalonia's selection model.
        // Publish the new projection once, retaining the same row objects for surviving lines.
        _displayRows = new AvaloniaList<LogLineRow>(desired);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLines)));
    }

    private bool UpdateDisplayedRanges(IReadOnlyList<LogLineRow> desired)
    {
        Span<(int Index, int Count, bool Insert)> edits = stackalloc (int, int, bool)[16];
        var editCount = 0;
        var oldIndex = 0;
        var newIndex = 0;
        // Both lists are in file order. Plan a bounded number of edits before touching the UI collection.
        while (oldIndex < _displayRows.Count || newIndex < desired.Count)
        {
            var oldRow = oldIndex < _displayRows.Count ? _displayRows[oldIndex] : null;
            var newRow = newIndex < desired.Count ? desired[newIndex] : null;
            if (ReferenceEquals(oldRow, newRow))
            {
                oldIndex++;
                newIndex++;
                continue;
            }
            if (editCount == edits.Length) return false;
            if (oldRow is not null && (newRow is null || oldRow.Number <= newRow.Number))
            {
                var start = oldIndex++;
                while (oldIndex < _displayRows.Count && (newRow is null || _displayRows[oldIndex].Number < newRow.Number))
                    oldIndex++;
                edits[editCount++] = (newIndex, oldIndex - start, false);
            }
            else
            {
                var start = newIndex++;
                while (newIndex < desired.Count && (oldRow is null || desired[newIndex].Number < oldRow.Number))
                    newIndex++;
                edits[editCount++] = (start, newIndex - start, true);
            }
        }

        foreach (var (index, count, insert) in edits[..editCount])
        {
            if (insert) _displayRows.InsertRange(index, desired.Skip(index).Take(count));
            else _displayRows.RemoveRange(index, count);
        }
        return true;
    }

    private void BeginOperation()
    {
        if (_isBusy) throw new InvalidOperationException("An operation is already running.");
        _projectionRevision++;
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
