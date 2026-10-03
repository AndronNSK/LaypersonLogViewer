using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FilterGroupTests
{
    private static LogFilter Group(FilterKind kind, string text, params string[] children) =>
        new(kind, new LogFilterCondition(text), children.Select(child => new LogFilterCondition(child)));

    private static LogLine[] Lines(params string[] texts) => texts.Select((text, i) => new LogLine(i + 1, text)).ToArray();

    [Fact]
    public void GroupsUseAndIncludesUseOrAndExclusionsWin()
    {
        var lines = Lines("ERROR database timeout", "ERROR database", "ERROR timeout", "WARN",
            "ERROR database timeout healthcheck", "INFO", "ERROR database timeout");
        var rules = new[] { Group(FilterKind.Include, "ERROR", "database", "timeout"),
            Group(FilterKind.Include, "WARN"), Group(FilterKind.Exclude, "healthcheck") };
        Assert.Equal(new[] { 1, 4, 7 }, LogFilterEngine.Apply(lines, rules, TestContext.Current.CancellationToken).Select(line => line.Number));
    }

    [Fact]
    public void ExclusionGroupRequiresAllConditionsWithoutIncludeGroups()
    {
        var lines = Lines("DEBUG heartbeat", "DEBUG other", "INFO heartbeat", "", "DEBUG heartbeat noisy");
        Assert.Equal(new[] { 2, 3, 4 }, LogFilterEngine.Apply(lines,
            new[] { Group(FilterKind.Exclude, "DEBUG", "heartbeat") }, TestContext.Current.CancellationToken).Select(line => line.Number));
    }

    [Fact]
    public void ConditionsHaveIndependentCaseSensitivityAndLiteralWhitespace()
    {
        var filter = new LogFilter(FilterKind.Include, new LogFilterCondition("error"),
            new[] { new LogFilterCondition(" DB.* ", true), new LogFilterCondition("TIMEOUT") });
        Assert.True(filter.Matches("ERROR DB.* timeout"));
        Assert.False(filter.Matches("ERROR db.* timeout"));
        Assert.False(filter.Matches("ERROR DBabc timeout"));
        Assert.False(filter.Matches("ERROR DB.*timeout"));
    }

    [Fact]
    public void EntryConditionsCanMatchDifferentLinesButNeverDifferentEntries()
    {
        var entries = new[] { new LogEntry(Lines("ERROR", "database", "timeout")),
            new LogEntry(Lines("ERROR database")), new LogEntry(Lines("timeout")) };
        var rules = new[] { Group(FilterKind.Include, "ERROR", "database", "timeout") };
        Assert.Same(entries[0], Assert.Single(LogFilterEngine.ApplyEntries(entries, rules, TestContext.Current.CancellationToken)));
        Assert.Empty(LogFilterEngine.Apply(entries[0].Lines, rules, TestContext.Current.CancellationToken));
        Assert.Equal(entries.Skip(1), LogFilterEngine.ApplyEntries(entries,
            new[] { Group(FilterKind.Exclude, "ERROR", "database", "timeout") }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GroupCopiesConditionsAndRejectsInvalidConditions()
    {
        var conditions = new List<LogFilterCondition> { new("database") };
        var group = new LogFilter(FilterKind.Include, new LogFilterCondition("ERROR"), conditions);
        conditions.Clear();
        Assert.Single(group.AdditionalConditions);
        Assert.Throws<ArgumentException>(() => new LogFilterCondition(" "));
        Assert.Throws<ArgumentException>(() => new LogFilter(FilterKind.Include,
            new LogFilterCondition("ERROR"), new LogFilterCondition[] { null! }));
    }
}
