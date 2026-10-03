namespace LaypersonLogViewer.Core;

public sealed record LogEntry(IReadOnlyList<LogLine> Lines);
