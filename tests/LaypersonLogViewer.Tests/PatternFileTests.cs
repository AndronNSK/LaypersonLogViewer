using System.Text;
using System.Text.Json;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class PatternFileTests
{
    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task FiltersRoundTripPreservesGroupsAndRefreshesLogAndStatistics()
    {
        var source = new MainWindowViewModel();
        using var sourceStats = source.Statistics;
        await source.AddFilterAsync(new LogFilter(FilterKind.Include, new("ERROR", true), [new("db", false)]));
        await source.AddFilterAsync(new LogFilter(FilterKind.Exclude, "skip", true));
        using var file = new MemoryStream();
        await source.SaveFiltersAsync(file);
        var target = new MainWindowViewModel();
        using var stats = target.Statistics;
        using var log = Text("ERROR DB x=2\nERROR DB skip x=9\nINFO x=4");
        await target.LoadAsync("example.log", log);
        stats.SavePattern(StatisticsTests.Pattern());
        await target.AddFilterAsync(new LogFilter(FilterKind.Include, "INFO"));
        file.Position = 0;
        await target.LoadFiltersAsync(file);
        await stats.CurrentCalculation;
        Assert.Equal(2, target.Filters.Count);
        Assert.True(target.Filters[0].CaseSensitive);
        Assert.False(Assert.Single(target.Filters[0].AdditionalConditions).CaseSensitive);
        Assert.Equal(FilterKind.Exclude, target.Filters[1].Kind);
        Assert.Equal("ERROR DB x=2", Assert.Single(target.VisibleLines).Text);
        Assert.Equal("2", Assert.Single(stats.Patterns[0].Values).Mean);
    }

    [Fact]
    public async Task StatisticsRequireExplicitLoadingAndRoundTripAllDefinitionFields()
    {
        using var source = new StatisticsViewModel();
        var ranges = new StatisticsRange[] { new(0, 2, StatisticsRangeKind.Anchor), new(2, 1, StatisticsRangeKind.Number, "value1") };
        var pattern = new StatisticsPattern(Guid.NewGuid(), "Время", StatisticsPatternBuilder.Build("x=2", ranges),
            true, "x=2", false, ranges, [new("value1", "Длительность")]);
        source.SavePattern(pattern);
        source.ScopeIndex = 1;
        using var file = new MemoryStream();
        await source.ExportAsync(file);
        using var target = new StatisticsViewModel();
        Assert.Empty(target.Patterns);
        Assert.Equal(0, target.ScopeIndex);
        target.SavePattern(StatisticsTests.Pattern());
        target.SetSources(StatisticsTests.Lines("x=8", "x=10"), StatisticsTests.Lines("x=8"), true);
        file.Position = 0;
        await target.ImportAsync(file);
        await target.CurrentCalculation;
        var row = Assert.Single(target.Patterns);
        Assert.Equal(pattern.Id, row.Pattern.Id);
        Assert.Equal(pattern.Name, row.Name);
        Assert.Equal(pattern.Expression, row.Expression);
        Assert.Equal(pattern.Ranges, row.Pattern.Ranges);
        Assert.Equal(pattern.Values, row.Pattern.Values);
        Assert.Equal(pattern.Example, row.Pattern.Example);
        Assert.False(row.Pattern.ManualRegex);
        Assert.True(row.Pattern.CaseSensitive);
        Assert.Equal(1, target.ScopeIndex);
        Assert.Equal("9", Assert.Single(row.Values).Mean);
        Assert.Null(target.SelectedPattern);
        Assert.DoesNotContain("Mean", Encoding.UTF8.GetString(file.ToArray()));
        using var nextLaunch = new StatisticsViewModel();
        Assert.Empty(nextLaunch.Patterns);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":999,\"Filters\":[]}")]
    [InlineData("{\"FormatVersion\":1,\"Filters\":[null]}")]
    [InlineData("{\"FormatVersion\":1,\"Filters\":[{\"Kind\":42,\"Condition\":{\"Text\":\"x\"},\"AdditionalConditions\":[]}]}")]
    [InlineData("{\"FormatVersion\":1,\"Filters\":[{\"Kind\":0,\"Condition\":{\"Text\":\" \"},\"AdditionalConditions\":[]}]}")]
    [InlineData("{\"FormatVersion\":1,\"Scope\":0,\"Patterns\":[]}")]
    public async Task InvalidFilterFileLeavesExistingGroupsUnchanged(string json)
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        var original = new LogFilter(FilterKind.Include, "original");
        await model.AddFilterAsync(original);
        using var file = Text(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => model.LoadFiltersAsync(file));
        Assert.Same(original, Assert.Single(model.Filters));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"FormatVersion\":999,\"Scope\":0,\"Patterns\":[]}")]
    [InlineData("{\"FormatVersion\":1,\"Scope\":42,\"Patterns\":[]}")]
    [InlineData("{\"FormatVersion\":1,\"Scope\":0,\"Patterns\":[null]}")]
    [InlineData("{\"FormatVersion\":1,\"Filters\":[]}")]
    public async Task InvalidStatisticsFileLeavesExistingPatternsAndScopeUnchanged(string json)
    {
        using var model = new StatisticsViewModel();
        model.SavePattern(StatisticsTests.Pattern());
        model.ScopeIndex = 1;
        var original = model.Patterns[0];
        using var file = Text(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => model.ImportAsync(file));
        Assert.Same(original, Assert.Single(model.Patterns));
        Assert.Equal(1, model.ScopeIndex);
    }

    [Fact]
    public async Task InvalidRegexAndDuplicateIdsAreRejectedBeforeReplacingPatterns()
    {
        var pattern = StatisticsTests.Pattern();
        foreach (var patterns in new[] { new[] { pattern with { Expression = "[" } }, new[] { pattern, pattern } })
        {
            using var input = Text(JsonSerializer.Serialize(new StatisticsSettings(1, StatisticsScope.Filtered, patterns)));
            await Assert.ThrowsAsync<InvalidDataException>(() => PatternFiles.LoadStatisticsAsync(input));
        }
    }

    [Fact]
    public async Task EmptyDocumentsClearBothSets()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "x"));
        stats.SavePattern(StatisticsTests.Pattern());
        using var filters = new MemoryStream();
        await PatternFiles.SaveFiltersAsync(filters, []);
        filters.Position = 0;
        await model.LoadFiltersAsync(filters);
        using var statistics = new MemoryStream();
        await PatternFiles.SaveStatisticsAsync(statistics, new(1, StatisticsScope.Filtered, []));
        statistics.Position = 0;
        await stats.ImportAsync(statistics);
        Assert.Empty(model.Filters);
        Assert.Empty(stats.Patterns);
    }

    [Fact]
    public async Task AtomicSaveReplacesExistingFileAndCleansTemporaryFilesOnFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PatternFiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "patterns.json");
            await PatternFiles.WriteFileAsync(path, Encoding.UTF8.GetBytes("old"));
            await PatternFiles.WriteFileAsync(path, Encoding.UTF8.GetBytes("new"));
            Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            // A directory cannot be replaced with a file, on Windows or Linux.
            var blocked = Path.Combine(directory, "blocked");
            Directory.CreateDirectory(blocked);
            var error = await Record.ExceptionAsync(() => PatternFiles.WriteFileAsync(blocked, [1]));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            Assert.Equal("new", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }
}
