using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify(string property = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed class StatisticsValueRow(ValueStatistics initialValue) : ObservableModel
{
    private ValueStatistics value = initialValue;
    public string GroupName => value.GroupName;
    public void Update(ValueStatistics updated)
    {
        if (value == updated) return;
        value = updated;
        Notify();
    }
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
    public void SetPending(bool loaded, bool liveUpdate = false)
    {
        if (liveUpdate && loaded && Values.Count > 0) return;
        Status = loaded ? "Расчёт…" : "Откройте файл";
        Values = [];
        Notify();
    }
    public void SetResult(PatternStatistics result)
    {
        Status = result.Error ?? "Готово";
        if (Values.Select(value => value.GroupName).SequenceEqual(result.Values.Select(value => value.GroupName)))
        {
            for (var i = 0; i < Values.Count; i++) Values[i].Update(result.Values[i]);
        }
        else
        {
            Values = result.Values.Select(value => new StatisticsValueRow(value)).ToArray();
            Notify(nameof(Values));
        }
        Notify(nameof(Status));
    }
}

public sealed class StatisticsViewModel : ObservableModel, IDisposable
{
    private readonly Func<StatisticsPattern, IReadOnlyList<LogLine>, CancellationToken, PatternStatistics> _calculate;
    private CancellationTokenSource? _calculation;
    private IReadOnlyList<LogLine> _all = [], _filtered = [];
    private bool _loaded, _disposed;
    private int _scopeIndex;
    private StatisticsPatternRow? _selected;
    public ObservableCollection<StatisticsPatternRow> Patterns { get; } = [];
    public Task CurrentCalculation { get; private set; } = Task.CompletedTask;
    public string[] ScopeLabels { get; } = ["После фильтрации", "Весь файл"];
    public int ScopeIndex
    {
        get => _scopeIndex;
        set
        {
            if (value is < 0 or > 1 || value == _scopeIndex) return;
            _scopeIndex = value;
            Notify();
            Recalculate();
        }
    }
    public StatisticsPatternRow? SelectedPattern
    {
        get => _selected;
        set { _selected = value; Notify(); }
    }
    public bool HasSelection => SelectedPattern is not null;
    public IReadOnlyList<LogLine> SourceLines => _scopeIndex == 0 ? _filtered : _all;

    public StatisticsViewModel(Func<StatisticsPattern, IReadOnlyList<LogLine>, CancellationToken, PatternStatistics>? calculate = null)
    {
        _calculate = calculate ?? StatisticsCalculator.Calculate;
    }

    public void SetSources(IReadOnlyList<LogLine> all, IReadOnlyList<LogLine> filtered, bool loaded, bool liveUpdate = false)
    {
        _all = all;
        _filtered = filtered;
        _loaded = loaded;
        Recalculate(liveUpdate);
    }

    public void SavePattern(StatisticsPattern pattern)
    {
        pattern.Validate();
        // Copy collections so an editor cannot change a running calculation.
        pattern = pattern with { Values = pattern.Values.ToArray(), Ranges = pattern.Ranges.ToArray() };
        var existing = Patterns.FirstOrDefault(row => row.Pattern.Id == pattern.Id);
        var row = new StatisticsPatternRow(pattern);
        if (existing is null) Patterns.Add(row);
        else { row.IsExpanded = existing.IsExpanded; Patterns[Patterns.IndexOf(existing)] = row; }
        SelectedPattern = row;
        Recalculate();
    }

    public void DeleteSelected()
    {
        if (SelectedPattern is null) return;
        Patterns.Remove(SelectedPattern);
        SelectedPattern = null;
        Recalculate();
    }

    public Task ExportAsync(Stream stream) => PatternFiles.SaveStatisticsAsync(stream, CreateSnapshot());

    public async Task ImportAsync(Stream stream)
    {
        var settings = await PatternFiles.LoadStatisticsAsync(stream);
        if (_disposed) return;
        Patterns.Clear();
        foreach (var pattern in settings.Patterns) Patterns.Add(new(pattern));
        _scopeIndex = (int)settings.Scope;
        SelectedPattern = null;
        Recalculate();
        Notify();
    }

    private StatisticsSettings CreateSnapshot() =>
        new(1, (StatisticsScope)_scopeIndex, Patterns.Select(row => row.Pattern).ToArray());

    private void Recalculate(bool liveUpdate = false)
    {
        _calculation?.Cancel();
        _calculation?.Dispose();
        _calculation = new CancellationTokenSource();
        var token = _calculation.Token;
        var rows = Patterns.ToArray();
        foreach (var row in rows) row.SetPending(_loaded, liveUpdate);
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
