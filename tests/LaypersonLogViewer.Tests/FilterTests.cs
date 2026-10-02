using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FilterTests
{
    private static readonly LogLine[] Lines =
    {
        new(1, "INFO Ready"), new(2, "ERROR server"), new(3, "WARN slow"),
        new(4, "error localhost"), new(5, ""), new(6, "ERROR server")
    };

    [Fact]
    public void NoFiltersPreservesAllLinesIncludingBlanksAndDuplicates() =>
        Assert.Equal(Lines, LogFilterEngine.Apply(Lines, Array.Empty<LogFilter>(), TestContext.Current.CancellationToken));

    [Fact]
    public void IncludesUseOrAndPreserveOriginalNumbers()
    {
        var result = LogFilterEngine.Apply(Lines, new[]
        {
            new LogFilter(FilterKind.Include, "ERROR"), new LogFilter(FilterKind.Include, "WARN")
        }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 2, 3, 4, 6 }, result.Select(l => l.Number));
    }

    [Fact]
    public void ExcludesOverrideIncludes()
    {
        var result = LogFilterEngine.Apply(Lines, new[]
        {
            new LogFilter(FilterKind.Include, "error"), new LogFilter(FilterKind.Exclude, "localhost")
        }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 2, 6 }, result.Select(l => l.Number));
    }

    [Fact]
    public void ExcludesAloneRemoveAnyMatch()
    {
        var result = LogFilterEngine.Apply(Lines, new[]
        {
            new LogFilter(FilterKind.Exclude, "error"), new LogFilter(FilterKind.Exclude, "warn")
        }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 1, 5 }, result.Select(l => l.Number));
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void CaseSensitivityIsOptional(bool caseSensitive, int count) =>
        Assert.Equal(count, LogFilterEngine.Apply(Lines,
            new[] { new LogFilter(FilterKind.Include, "ERROR", caseSensitive) }, TestContext.Current.CancellationToken).Count);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(null)]
    public void EmptyFiltersAreRejected(string? text) =>
        Assert.ThrowsAny<ArgumentException>(() => new LogFilter(FilterKind.Include, text!));

    [Fact]
    public void LiteralTextPreservesSpacesAndDoesNotInterpretRegex()
    {
        var filter = new LogFilter(FilterKind.Include, " [a.*] ");
        Assert.True(filter.Matches("prefix [a.*] suffix"));
        Assert.False(filter.Matches("[a.*]"));
        Assert.False(filter.Matches("prefix aaa suffix"));
    }

    [Fact]
    public void CyrillicMatchingIgnoresCaseByDefault() =>
        Assert.True(new LogFilter(FilterKind.Include, "ошибка").Matches("ОШИБКА подключения"));

    [Fact]
    public void NoMatchReturnsEmptyList() =>
        Assert.Empty(LogFilterEngine.Apply(Lines, new[] { new LogFilter(FilterKind.Include, "missing") }, TestContext.Current.CancellationToken));

    [Fact]
    public void CancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => LogFilterEngine.Apply(
            Lines, Array.Empty<LogFilter>(), cancellation.Token));
    }
}
