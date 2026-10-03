using System.Text.RegularExpressions;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class StatisticsTests
{
    internal static StatisticsPattern Pattern(string expression = @"x=(?<x>\S+)") => new(Guid.NewGuid(), "Test", expression,
        true, "", true, [], StatisticsPattern.NamedGroups(new Regex(expression, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            .Select(name => new StatisticsValue(name, name)).ToArray());
    internal static LogLine[] Lines(params string[] texts) => texts.Select((text, i) => new LogLine(i + 1, text)).ToArray();

    [Theory]
    [InlineData("+12", 12)]
    [InlineData(" -12,5 ", -12.5)]
    [InlineData(".5", .5)]
    [InlineData("1,2e3", 1200)]
    [InlineData("-2.5E-2", -.025)]
    public void NumericFormats(string text, double expected)
    {
        Assert.True(StatisticsCalculator.TryParseNumber(text, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    [InlineData("1 000")]
    [InlineData("1,234.5")]
    [InlineData("12ms")]
    [InlineData("")]
    public void InvalidNumbersAreRejected(string text) => Assert.False(StatisticsCalculator.TryParseNumber(text, out _));

    [Fact]
    public void FirstMatchOnlyIndependentCountsAndSkippedValues()
    {
        var result = StatisticsCalculator.Calculate(Pattern(@"x=(?<x>\S+)(?: y=(?<y>\S+))?"),
            Lines("x=1 y=10 x=99 y=99", "x=3 y=bad", "x=3", "x=bad y=20", "unrelated"), TestContext.Current.CancellationToken);
        Assert.Null(result.Error);
        var x = result.Values[0];
        Assert.Equal(3, x.Count);
        Assert.Equal(1, x.Skipped);
        Assert.Equal(7d / 3, x.Mean!.Value, 12);
        Assert.Equal(1, x.Minimum);
        Assert.Equal(3, x.Maximum);
        Assert.Equal(3, x.Median);
        var y = result.Values[1];
        Assert.Equal(2, y.Count);
        Assert.Equal(2, y.Skipped);
        Assert.Equal(15, y.Mean);
        Assert.Equal(15, y.Median);
    }

    [Fact]
    public void EmptyAndExtremeResultsAreFinite()
    {
        var empty = Assert.Single(StatisticsCalculator.Calculate(Pattern(), Lines("unrelated"), TestContext.Current.CancellationToken).Values);
        Assert.Equal(0, empty.Count);
        Assert.Null(empty.Mean);
        Assert.Null(empty.Minimum);
        Assert.Null(empty.Maximum);
        Assert.Null(empty.Median);
        var values = Assert.Single(StatisticsCalculator.Calculate(Pattern(), Lines("x=-1e308", "x=1e308"), TestContext.Current.CancellationToken).Values);
        Assert.Equal(0, values.Mean);
        Assert.Equal(0, values.Median);
        var huge = Assert.Single(StatisticsCalculator.Calculate(Pattern(), Lines("x=1e308", "x=1e308"), TestContext.Current.CancellationToken).Values);
        Assert.Equal(1e308, huge.Mean);
        Assert.Equal(1e308, huge.Median);
    }

    [Fact]
    public void BuilderEscapesAnchorsOrdersRangesAndIgnoresUnmarkedText()
    {
        const string example = "prefix [req]. duration=12.5 size=20 suffix";
        var ranges = new[] { new StatisticsRange(example.IndexOf("20", StringComparison.Ordinal), 2, StatisticsRangeKind.Number, "size"),
            new StatisticsRange(example.IndexOf("[req].", StringComparison.Ordinal), 6, StatisticsRangeKind.Anchor),
            new StatisticsRange(example.IndexOf("12.5", StringComparison.Ordinal), 4, StatisticsRangeKind.Number, "duration") };
        var expression = StatisticsPatternBuilder.Build(example, ranges);
        Assert.Contains(Regex.Escape("[req]."), expression);
        var result = StatisticsCalculator.Calculate(Pattern(expression), Lines("other [req]. duration=30,5 size=99"), TestContext.Current.CancellationToken);
        Assert.Equal(new double?[] { 30.5, 99 }, result.Values.Select(v => v.Mean));
        Assert.Equal(0, StatisticsCalculator.Calculate(Pattern(expression), Lines("reqX duration=30 size=99"), TestContext.Current.CancellationToken).Values[0].Count);
        Assert.Throws<ArgumentException>(() => StatisticsPatternBuilder.Build(example, [ranges[0]]));
        Assert.Throws<ArgumentException>(() => StatisticsPatternBuilder.Build(example,
            [ranges[1], new StatisticsRange(ranges[1].Start, 2, StatisticsRangeKind.Number, "x")]));
        Assert.Throws<ArgumentException>(() => StatisticsPatternBuilder.Build(example,
            [ranges[1], new StatisticsRange(100, 3, StatisticsRangeKind.Number, "x")]));
    }

    [Fact]
    public void InvalidRegexTimeoutAndCancellationDoNotProducePartialStatistics()
    {
        var invalid = Pattern() with { Expression = "[" };
        Assert.NotNull(StatisticsCalculator.Calculate(invalid, Lines("x=1"), TestContext.Current.CancellationToken).Error);
        Assert.Throws<ArgumentException>(() => Pattern("(123)").Validate());
        var timeout = StatisticsCalculator.Calculate(Pattern(@"(?<x>(a+)+)$"),
            Lines(new string('a', 20000) + "!"), TestContext.Current.CancellationToken);
        Assert.NotNull(timeout.Error);
        Assert.Empty(timeout.Values);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => StatisticsCalculator.Calculate(Pattern(), Lines("x=1"), cancellation.Token));
    }

    [Fact]
    public void PreviewUsesFirstMatchingLineAndReportsMissingOrInvalidNumbers()
    {
        var preview = StatisticsCalculator.Preview(Pattern(), Lines("none", "x=bad", "x=3"), TestContext.Current.CancellationToken);
        Assert.Equal(2, preview.LineNumber);
        Assert.Contains("пропущено", Assert.Single(preview.Values));
        Assert.Null(StatisticsCalculator.Preview(Pattern(), Lines("none"), TestContext.Current.CancellationToken).LineNumber);
    }
}
