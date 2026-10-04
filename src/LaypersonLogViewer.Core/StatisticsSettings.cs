namespace LaypersonLogViewer.Core;

public sealed record StatisticsSettings(int FormatVersion, StatisticsScope Scope, IReadOnlyList<StatisticsPattern> Patterns);
