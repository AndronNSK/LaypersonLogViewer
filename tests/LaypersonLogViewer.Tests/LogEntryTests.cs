using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class LogEntryTests
{
    private static readonly TimestampPattern Pattern = new("2026-10-03 12:34:56", 0);
    private static LogLine[] Lines(params string[] lines) => lines.Select((text, i) => new LogLine(i + 1, text)).ToArray();

    [Theory]
    [InlineData("[2027-01-09 01:02:03.987] INFO", true)]
    [InlineData("[2027/01/09 01:02:03.987] INFO", false)]
    [InlineData(" [2027-01-09 01:02:03.987] INFO", false)]
    [InlineData("[2027-01-09 01:02:03.a87] INFO", false)]
    [InlineData("short", false)]
    [InlineData("", false)]
    public void PatternMatchesDigitsSeparatorsAndExactPosition(string text, bool matches)
    {
        var pattern = new TimestampPattern("2026-10-03 12:34:56.123", 1);
        Assert.Equal(matches, pattern.Matches(text));
        Assert.Equal("####-##-## ##:##:##.###", pattern.Shape);
    }

    [Fact]
    public void PatternTreatsPunctuationLiterallyAndDoesNotValidateDates()
    {
        var pattern = new TimestampPattern("[12:34:56]", 0);
        Assert.True(pattern.Matches("[99:99:99] event"));
        Assert.False(pattern.Matches("X99:99:99X event"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("timestamp")]
    [InlineData("   ")]
    [InlineData("12\n34")]
    public void InvalidExamplesAreRejected(string example) =>
        Assert.ThrowsAny<ArgumentException>(() => new TimestampPattern(example, 0));

    [Fact]
    public void NegativePositionIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TimestampPattern("12:34", -1));

    [Fact]
    public void SplitPreservesPreambleBlankLinesContinuationsAndOriginalNumbers()
    {
        var lines = Lines("preamble", "", "2026-10-03 12:34:56 ERROR", " stack trace", "",
            "2026-10-03 12:34:57 INFO", "details");
        var entries = LogEntryParser.Split(lines, Pattern, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 2, 3, 2 }, entries.Select(e => e.Lines.Count));
        Assert.Equal(lines, entries.SelectMany(e => e.Lines));
        Assert.Equal(new[] { 1, 3, 6 }, entries.Select(e => e.Lines[0].Number));
    }

    [Fact]
    public void NoPatternKeepsLineByLineMode()
    {
        var entries = LogEntryParser.Split(Lines("ERROR", " stack trace", ""), null, TestContext.Current.CancellationToken);
        Assert.Equal(3, entries.Count);
        Assert.All(entries, entry => Assert.Single(entry.Lines));
    }

    [Fact]
    public void NoMatchingTimestampsKeepsAllLinesInOneEntry()
    {
        var lines = Lines("INFO", "details", "");
        Assert.Equal(lines, Assert.Single(LogEntryParser.Split(lines, Pattern, TestContext.Current.CancellationToken)).Lines);
        Assert.Empty(LogEntryParser.Split(Array.Empty<LogLine>(), Pattern, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IncludeOnContinuationKeepsWholeEntryAndExcludeOverridesIt()
    {
        var entries = LogEntryParser.Split(Lines("2026-10-03 12:34:56 ERROR", " socket timeout", " ignore",
            "2026-10-03 12:34:57 INFO", " socket timeout", "2026-10-03 12:34:58 WARN"),
            Pattern, TestContext.Current.CancellationToken);
        var filters = new[] { new LogFilter(FilterKind.Include, "timeout"),
            new LogFilter(FilterKind.Include, "WARN"), new LogFilter(FilterKind.Exclude, "ignore") };
        var matching = LogFilterEngine.ApplyEntries(entries, filters, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 4, 5, 6 }, matching.SelectMany(e => e.Lines).Select(l => l.Number));
    }

    [Fact]
    public void NoFiltersKeepAllEntriesAndLiteralSearchDoesNotCrossLineBreaks()
    {
        var entries = LogEntryParser.Split(Lines("2026-10-03 12:34:56 ERROR", "details"),
            Pattern, TestContext.Current.CancellationToken);
        Assert.Equal(entries, LogFilterEngine.ApplyEntries(entries, Array.Empty<LogFilter>(), TestContext.Current.CancellationToken));
        Assert.Empty(LogFilterEngine.ApplyEntries(entries, new[] { new LogFilter(FilterKind.Include, "ERRORdetails") },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SplittingAndFilteringObserveCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var lines = Lines("one", "two");
        Assert.Throws<OperationCanceledException>(() => LogEntryParser.Split(lines, Pattern, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => LogFilterEngine.ApplyEntries(
            new[] { new LogEntry(lines) }, Array.Empty<LogFilter>(), cancellation.Token));
    }
}
