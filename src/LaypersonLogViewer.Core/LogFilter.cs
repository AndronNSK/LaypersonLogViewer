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

    public bool Matches(string line) => Condition.Matches(line)
        && AdditionalConditions.All(condition => condition.Matches(line));

    public bool Matches(LogEntry entry, CancellationToken cancellationToken = default)
    {
        bool MatchesCondition(LogFilterCondition condition)
        {
            foreach (var line in entry.Lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (condition.Matches(line.Text)) return true;
            }
            return false;
        }

        return MatchesCondition(Condition) && AdditionalConditions.All(MatchesCondition);
    }
}
