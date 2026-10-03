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
            var included = includes.Length == 0;
            var excluded = false;
            foreach (var line in entry.Lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                included |= includes.Any(f => f.Matches(line.Text));
                if (excludes.Any(f => f.Matches(line.Text)))
                {
                    excluded = true;
                    break;
                }
            }
            if (included && !excluded) result.Add(entry);
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
