using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class StatisticsValueEditor(string groupName, string displayName, Action changed) : ObservableModel
{
    public string GroupName { get; } = groupName;
    private string _displayName = displayName;
    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName == value) return; _displayName = value; Notify(); changed(); }
    }
}

public sealed record StatisticsRangeRow(StatisticsRange Range, string Text)
{
    public string Description => Range.Kind == StatisticsRangeKind.Anchor
        ? $"Постоянный текст: {Text}" : $"Число ({Range.GroupName}): {Text}";
}

public sealed class StatisticsEditorViewModel : ObservableModel, IDisposable
{
    private readonly Guid _id;
    private readonly IReadOnlyList<LogLine> _lines;
    private CancellationTokenSource? _preview;
    private string _name, _expression;
    private bool _caseSensitive, _manualRegex, _updating;
    private int _nextGroup = 1;
    public string Example { get; }
    public ObservableCollection<StatisticsRangeRow> Ranges { get; } = [];
    public ObservableCollection<StatisticsValueEditor> Values { get; } = [];
    public string Name { get => _name; set { _name = value; Refresh(); } }
    public string Expression { get => _expression; set { _expression = value; Refresh(); } }
    public bool CaseSensitive { get => _caseSensitive; set { _caseSensitive = value; Refresh(); } }
    public bool ManualRegex => _manualRegex;
    public bool CanMark => !ManualRegex && Example.Length > 0;
    public bool CanSave { get; private set; }
    public string? Error { get; private set; }
    public string PreviewText { get; private set; } = "";
    public Task PreviewTask { get; private set; } = Task.CompletedTask;

    public StatisticsEditorViewModel(string example, IReadOnlyList<LogLine> lines, StatisticsPattern? pattern = null)
    {
        _id = pattern?.Id ?? Guid.NewGuid();
        Example = pattern?.Example ?? example;
        _lines = lines;
        _name = pattern?.Name ?? "Новая статистика";
        _expression = pattern?.Expression ?? "";
        _caseSensitive = pattern?.CaseSensitive ?? true;
        _manualRegex = pattern?.ManualRegex ?? Example.Length == 0;
        foreach (var range in pattern?.Ranges ?? [])
            Ranges.Add(new(range, range.Start >= 0 && range.Length > 0 && range.Start <= Example.Length - range.Length
                ? Example.Substring(range.Start, range.Length) : ""));
        foreach (var value in pattern?.Values ?? []) Values.Add(new(value.GroupName, value.DisplayName, Refresh));
        Refresh();
    }

    public void SetManualRegex(bool manual)
    {
        _manualRegex = manual;
        Refresh();
    }

    public bool AddRange(int start, int length, StatisticsRangeKind kind, string displayName = "")
    {
        if (!CanMark) return false;
        if (start < 0 || length <= 0 || start > Example.Length - length
            || Ranges.Any(row => start < row.Range.Start + row.Range.Length && start + length > row.Range.Start))
        { Error = "Выделите непересекающийся фрагмент строки."; Notify(); return false; }
        if (kind == StatisticsRangeKind.Number && (!StatisticsCalculator.TryParseNumber(Example.Substring(start, length), out _)
                || string.IsNullOrWhiteSpace(displayName)))
        { Error = "Выделите число без единиц измерения и укажите его название."; Notify(); return false; }
        string group = "";
        if (kind == StatisticsRangeKind.Number)
        {
            do { group = $"value{_nextGroup++}"; } while (Values.Any(v => v.GroupName == group));
            Values.Add(new(group, displayName, Refresh));
        }
        var range = new StatisticsRange(start, length, kind, group);
        Ranges.Add(new(range, Example.Substring(start, length)));
        Refresh();
        return true;
    }

    public void RemoveRange(StatisticsRangeRow row)
    {
        if (ManualRegex) return;
        Ranges.Remove(row);
        if (row.Range.Kind == StatisticsRangeKind.Number)
        {
            var value = Values.FirstOrDefault(v => v.GroupName == row.Range.GroupName);
            if (value is not null) Values.Remove(value);
        }
        Refresh();
    }

    public StatisticsPattern BuildPattern() => new(_id, Name, Expression, CaseSensitive, Example, ManualRegex,
        Ranges.Select(row => row.Range).ToArray(), Values.Select(v => new StatisticsValue(v.GroupName, v.DisplayName)).ToArray());

    private void Refresh()
    {
        if (_updating) return;
        _updating = true;
        _preview?.Cancel();
        _preview?.Dispose();
        _preview = new CancellationTokenSource();
        try
        {
            if (!ManualRegex)
            {
                _expression = "";
                _expression = StatisticsPatternBuilder.Build(Example, Ranges.Select(row => row.Range));
            }
            var regex = new Regex(Expression, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var names = StatisticsPattern.NamedGroups(regex);
            foreach (var value in Values.Where(value => !names.Contains(value.GroupName)).ToArray()) Values.Remove(value);
            foreach (var name in names)
                if (!Values.Any(value => value.GroupName == name)) Values.Add(new(name, name, Refresh));
            var pattern = BuildPattern();
            pattern.Validate();
            CanSave = true;
            Error = null;
            PreviewText = "Предпросмотр…";
            PreviewTask = PreviewAsync(pattern, _preview.Token);
        }
        catch (ArgumentException e)
        {
            CanSave = false;
            Error = e.Message;
            PreviewText = "";
            PreviewTask = Task.CompletedTask;
        }
        finally { _updating = false; Notify(); }
    }

    private async Task PreviewAsync(StatisticsPattern pattern, CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
            var result = await Task.Run(() => StatisticsCalculator.Preview(pattern, _lines, token), token);
            token.ThrowIfCancellationRequested();
            PreviewText = result.Error ?? (result.LineNumber is null ? "Совпадений в выбранной области нет."
                : $"Строка {result.LineNumber}: {result.Text}\n" + string.Join(" · ", result.Values));
            Notify();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Dispose() { _preview?.Cancel(); _preview?.Dispose(); }
}
