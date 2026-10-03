using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class StatisticsValueRow(ValueStatistics value)
{
    public string Name => value.DisplayName;
    public int Count => value.Count;
    public string Mean => Format(value.Mean);
    public string Minimum => Format(value.Minimum);
    public string Maximum => Format(value.Maximum);
    public string Median => Format(value.Median);
    private static string Format(double? number) => number?.ToString("0.######", CultureInfo.CurrentCulture) ?? "—";
    private static string Exact(double? number) => number?.ToString("G17", CultureInfo.CurrentCulture) ?? "—";
    public string Details => $"Группа: {value.GroupName}\nПропущено: {value.Skipped}\nСреднее: {Exact(value.Mean)}\nМинимум: {Exact(value.Minimum)}\nМаксимум: {Exact(value.Maximum)}\nМедиана: {Exact(value.Median)}";
}

public sealed class StatisticsPatternRow(StatisticsPattern pattern) : ObservableModel
{
    public StatisticsPattern Pattern { get; } = pattern;
    public string Name => Pattern.Name;
    public string Expression => Pattern.Expression;
    public string Status { get; private set; } = "Откройте файл";
    public IReadOnlyList<StatisticsValueRow> Values { get; private set; } = [];
    public bool IsExpanded { get; set; } = true;
    public void SetPending(bool loaded)
    {
        Status = loaded ? "Расчёт…" : "Откройте файл";
        Values = [];
        Notify();
    }
    public void SetResult(PatternStatistics result)
    {
        Status = result.Error ?? "Готово";
        Values = result.Values.Select(value => new StatisticsValueRow(value)).ToArray();
        Notify();
    }
}

public sealed class StatisticsViewModel : ObservableModel, IDisposable
{
    private readonly IStatisticsSettingsStore _store;
    private readonly Func<StatisticsPattern, IReadOnlyList<LogLine>, CancellationToken, PatternStatistics> _calculate;
    private readonly SemaphoreSlim _saveLock = new(1);
    private CancellationTokenSource? _calculation;
    private IReadOnlyList<LogLine> _all = [], _filtered = [];
    private bool _loaded, _initialized, _disposed;
    private int _scopeIndex;
    private StatisticsPatternRow? _selected;
    public ObservableCollection<StatisticsPatternRow> Patterns { get; } = [];
    public Task Initialization { get; }
    public Task CurrentCalculation { get; private set; } = Task.CompletedTask;
    public Task PendingSave { get; private set; } = Task.CompletedTask;
    public string? Error { get; private set; }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsReady => _initialized;
    public string[] ScopeLabels { get; } = ["После фильтрации", "Весь файл"];
    public int ScopeIndex
    {
        get => _scopeIndex;
        set
        {
            if (value is < 0 or > 1 || value == _scopeIndex) return;
            _scopeIndex = value;
            Notify();
            if (_initialized) { Recalculate(); QueueSave(); }
        }
    }
    public StatisticsPatternRow? SelectedPattern
    {
        get => _selected;
        set { _selected = value; Notify(); }
    }
    public bool HasSelection => SelectedPattern is not null;
    public IReadOnlyList<LogLine> SourceLines => _scopeIndex == 0 ? _filtered : _all;

    public StatisticsViewModel(IStatisticsSettingsStore? store = null,
        Func<StatisticsPattern, IReadOnlyList<LogLine>, CancellationToken, PatternStatistics>? calculate = null)
    {
        _store = store ?? new MemoryStatisticsSettingsStore();
        _calculate = calculate ?? StatisticsCalculator.Calculate;
        Initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var settings = await _store.LoadAsync();
            if (_disposed) return;
            _scopeIndex = (int)settings.Scope;
            foreach (var pattern in settings.Patterns) Patterns.Add(new(pattern));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Error = $"Настройки статистики: {e.Message}"; }
        _initialized = true;
        Notify();
        Recalculate();
    }

    public void SetSources(IReadOnlyList<LogLine> all, IReadOnlyList<LogLine> filtered, bool loaded)
    {
        _all = all;
        _filtered = filtered;
        _loaded = loaded;
        if (_initialized) Recalculate();
    }

    public void SavePattern(StatisticsPattern pattern)
    {
        if (!_initialized) throw new InvalidOperationException("Настройки ещё загружаются.");
        pattern.Validate();
        // Copy collections so an editor cannot change a running calculation or queued save.
        pattern = pattern with { Values = pattern.Values.ToArray(), Ranges = pattern.Ranges.ToArray() };
        var existing = Patterns.FirstOrDefault(row => row.Pattern.Id == pattern.Id);
        var row = new StatisticsPatternRow(pattern);
        if (existing is null) Patterns.Add(row);
        else { row.IsExpanded = existing.IsExpanded; Patterns[Patterns.IndexOf(existing)] = row; }
        SelectedPattern = row;
        Recalculate();
        QueueSave();
    }

    public void DeleteSelected()
    {
        if (SelectedPattern is null) return;
        Patterns.Remove(SelectedPattern);
        SelectedPattern = null;
        Recalculate();
        QueueSave();
    }

    private void QueueSave()
    {
        var snapshot = new StatisticsSettings(1, (StatisticsScope)_scopeIndex, Patterns.Select(row => row.Pattern).ToArray());
        PendingSave = SaveAsync(snapshot);
    }

    private async Task SaveAsync(StatisticsSettings settings)
    {
        await _saveLock.WaitAsync();
        try { await _store.SaveAsync(settings); Error = null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Error = $"Не удалось сохранить статистику: {e.Message}"; }
        finally { _saveLock.Release(); Notify(); }
    }

    private void Recalculate()
    {
        _calculation?.Cancel();
        _calculation?.Dispose();
        _calculation = new CancellationTokenSource();
        var token = _calculation.Token;
        var rows = Patterns.ToArray();
        foreach (var row in rows) row.SetPending(_loaded);
        CurrentCalculation = _loaded && !_disposed ? CalculateAsync(rows, SourceLines, token) : Task.CompletedTask;
    }

    private async Task CalculateAsync(StatisticsPatternRow[] rows, IReadOnlyList<LogLine> lines, CancellationToken token)
    {
        try
        {
            foreach (var row in rows)
            {
                var result = await Task.Run(() => _calculate(row.Pattern, lines, token), token);
                token.ThrowIfCancellationRequested();
                row.SetResult(result);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _disposed = true;
        _calculation?.Cancel();
        _calculation?.Dispose();
    }
}
