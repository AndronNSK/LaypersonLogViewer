namespace LaypersonLogViewer.Core;

public sealed record LogSearchMatch(LogLine Line, int Start, int Length, bool Wrapped);

public static class LogSearch
{
    public static IEnumerable<(int Start, int Length)> HighlightRanges(string line, string text, bool caseSensitive)
    {
        if (text.Length == 0) yield break;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var start = -1;
        var end = -1;
        for (var offset = 0; offset <= line.Length - text.Length;)
        {
            var match = line.IndexOf(text, offset, comparison);
            if (match < 0) break;
            if (match > end)
            {
                if (start >= 0) yield return (start, end - start);
                start = match;
            }
            end = match + text.Length;
            offset = match + 1;
        }
        if (start >= 0) yield return (start, end - start);
    }

    // Lines are an immutable snapshot in ascending original line-number order.
    // Find only the next occurrence, without allocating a list of every match.
    public static LogSearchMatch? Find(IReadOnlyList<LogLine> lines, string text,
        bool caseSensitive = false, LogSearchMatch? current = null, bool backwards = false,
        CancellationToken cancellationToken = default, LogLine? startLine = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (lines.Count == 0 || text.Length == 0) return null;
        var currentIndex = current is null ? -1 : IndexOfLine(lines, current.Line);
        var index = currentIndex;
        if (index < 0 && startLine is not null) index = IndexOfLine(lines, startLine);
        if (index < 0) index = backwards ? lines.Count - 1 : 0;
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var wrapped = false;
        var passes = lines.Count + (currentIndex >= 0 ? 1 : 0);
        for (var pass = 0; pass < passes; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[index];
            var start = pass == 0 && currentIndex >= 0 && !backwards ? current!.Start + current.Length : 0;
            var end = pass == 0 && currentIndex >= 0 && backwards ? current!.Start : line.Text.Length;
            var position = backwards
                ? line.Text.AsSpan(0, end).LastIndexOf(text.AsSpan(), comparison)
                : line.Text.AsSpan(start).IndexOf(text.AsSpan(), comparison);
            if (position >= 0) return new(line, start + position, text.Length, wrapped);
            index += backwards ? -1 : 1;
            if (index < 0) { index = lines.Count - 1; wrapped = true; }
            else if (index == lines.Count) { index = 0; wrapped = true; }
        }
        return null;
    }

    public static int IndexOfLine(IReadOnlyList<LogLine> lines, LogLine line)
    {
        var low = 0;
        var high = lines.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var candidate = lines[middle];
            if (candidate.Number == line.Number) return ReferenceEquals(candidate, line) ? middle : -1;
            if (candidate.Number < line.Number) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }
}
