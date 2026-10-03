namespace LaypersonLogViewer.Core;

public static class LogEntryParser
{
    public static IReadOnlyList<LogEntry> Split(IReadOnlyList<LogLine> lines,
        TimestampPattern? pattern, CancellationToken cancellationToken = default)
    {
        var entries = new List<LogEntry>();
        List<LogLine>? current = null;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || pattern is null || pattern.Matches(line.Text))
            {
                current = new List<LogLine>();
                entries.Add(new LogEntry(current));
            }
            // Text before the first timestamp is preserved as a separate entry.
            current.Add(line);
        }
        return entries;
    }
}
