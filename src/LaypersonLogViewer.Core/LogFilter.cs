namespace LaypersonLogViewer.Core;

public enum FilterKind { Include, Exclude }

public sealed class LogFilter
{
    public FilterKind Kind { get; }
    public LogFilterCondition Condition { get; }
    public IReadOnlyList<LogFilterCondition> AdditionalConditions { get; }
    public string Text => Condition.Text;
    public bool CaseSensitive => Condition.CaseSensitive;
    public string KindLabel => Kind == FilterKind.Include ? "Показать" : "Скрыть";
    public string CaseLabel => Condition.CaseLabel;

    public LogFilter(FilterKind kind, string text, bool caseSensitive = false)
        : this(kind, new LogFilterCondition(text, caseSensitive), []) { }

    public LogFilter(FilterKind kind, LogFilterCondition condition,
        IEnumerable<LogFilterCondition> additionalConditions)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(additionalConditions);
        Kind = kind;
        Condition = condition;
        var children = additionalConditions.ToArray();
        if (children.Any(child => child is null)) throw new ArgumentException("Conditions cannot be null.", nameof(additionalConditions));
        AdditionalConditions = Array.AsReadOnly(children);
    }

    public bool Matches(string line)
    {
        if (!Condition.Matches(line)) return false;
        for (var index = 0; index < AdditionalConditions.Count; index++)
            if (!AdditionalConditions[index].Matches(line)) return false;
        return true;
    }

    public bool Matches(LogEntry entry, CancellationToken cancellationToken = default) =>
        Matches(entry.Lines, 0, entry.Lines.Count, cancellationToken);

    internal bool Matches(IReadOnlyList<LogLine> lines, int start, int end, CancellationToken cancellationToken)
    {
        bool MatchesCondition(LogFilterCondition condition)
        {
            for (var index = start; index < end; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (condition.Matches(lines[index].Text)) return true;
            }
            return false;
        }

        if (!MatchesCondition(Condition)) return false;
        for (var index = 0; index < AdditionalConditions.Count; index++)
            if (!MatchesCondition(AdditionalConditions[index])) return false;
        return true;
    }
}
