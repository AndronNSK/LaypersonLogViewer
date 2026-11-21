namespace LaypersonLogViewer.Core;

public static class LogFilterEngine
{
    public static IReadOnlyList<LogEntry> ApplyEntries(
        IReadOnlyList<LogEntry> entries, IEnumerable<LogFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var rules = new Rules(filters);
        var result = new List<LogEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rules.Matches(entry.Lines, 0, entry.Lines.Count, cancellationToken))
                result.Add(entry);
        }
        return result;
    }

    public static IReadOnlyList<LogLine> Apply(
        IReadOnlyList<LogLine> lines, IEnumerable<LogFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var rules = new Rules(filters);
        var result = new List<LogLine>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rules.Matches(line.Text)) result.Add(line);
        }

        return result;
    }

    // Each result covers [Start, End) in the source snapshot, without allocating an entry or a line list.
    public static IEnumerable<(int Start, int End, bool Matches, bool HasTimestamp)> Evaluate(
        IReadOnlyList<LogLine> lines, IEnumerable<LogFilter> filters, TimestampPattern? pattern)
    {
        var rules = new Rules(filters);
        if (pattern is null)
        {
            for (var index = 0; index < lines.Count; index++)
                yield return (index, index + 1, rules.Matches(lines[index].Text), false);
            yield break;
        }

        var hasTimestamp = lines.Count > 0 && pattern.Matches(lines[0].Text);
        for (var start = 0; start < lines.Count;)
        {
            var end = start + 1;
            while (end < lines.Count && !pattern.Matches(lines[end].Text)) end++;
            var matches = end == start + 1
                ? rules.Matches(lines[start].Text)
                : rules.Matches(lines, start, end, CancellationToken.None);
            yield return (start, end, matches, hasTimestamp);
            start = end;
            hasTimestamp = true;
        }
    }

    private sealed class Rules
    {
        private readonly LogFilter[] _includes;
        private readonly LogFilter[] _excludes;

        public Rules(IEnumerable<LogFilter> filters)
        {
            var rules = filters.ToArray();
            _includes = Array.FindAll(rules, filter => filter.Kind == FilterKind.Include);
            _excludes = Array.FindAll(rules, filter => filter.Kind == FilterKind.Exclude);
        }

        public bool Matches(string line)
        {
            foreach (var filter in _excludes)
                if (filter.Matches(line)) return false;
            foreach (var filter in _includes)
                if (filter.Matches(line)) return true;
            return _includes.Length == 0;
        }

        public bool Matches(IReadOnlyList<LogLine> lines, int start, int end, CancellationToken cancellationToken)
        {
            foreach (var filter in _excludes)
                if (filter.Matches(lines, start, end, cancellationToken)) return false;
            foreach (var filter in _includes)
                if (filter.Matches(lines, start, end, cancellationToken)) return true;
            return _includes.Length == 0;
        }
    }
}
