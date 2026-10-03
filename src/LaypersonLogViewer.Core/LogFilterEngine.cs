namespace LaypersonLogViewer.Core;

public static class LogFilterEngine
{
    public static IReadOnlyList<LogEntry> ApplyEntries(
        IReadOnlyList<LogEntry> entries, IEnumerable<LogFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var rules = filters.ToArray();
        var includes = rules.Where(f => f.Kind == FilterKind.Include).ToArray();
        var excludes = rules.Where(f => f.Kind == FilterKind.Exclude).ToArray();
        var result = new List<LogEntry>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((includes.Length == 0 || includes.Any(f => f.Matches(entry, cancellationToken)))
                && !excludes.Any(f => f.Matches(entry, cancellationToken)))
                result.Add(entry);
        }
        return result;
    }

    public static IReadOnlyList<LogLine> Apply(
        IReadOnlyList<LogLine> lines, IEnumerable<LogFilter> filters,
        CancellationToken cancellationToken = default)
    {
        var rules = filters.ToArray();
        var includes = rules.Where(f => f.Kind == FilterKind.Include).ToArray();
        var excludes = rules.Where(f => f.Kind == FilterKind.Exclude).ToArray();
        var result = new List<LogLine>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((includes.Length == 0 || includes.Any(f => f.Matches(line.Text)))
                && !excludes.Any(f => f.Matches(line.Text)))
                result.Add(line);
        }

        return result;
    }
}
