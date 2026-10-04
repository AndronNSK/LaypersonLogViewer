using System.Text;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class StatisticsViewModelTests
{
    [Fact]
    public async Task ScopeFollowsEntryFilteringButNotGreyDisplayAndRefreshesAfterFileLoad()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        stats.SavePattern(StatisticsTests.Pattern());
        Assert.Equal("Откройте файл", stats.Patterns[0].Status);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12:00 ERROR\nx=2\n12:01 INFO\nx=10"));
        await model.LoadAsync("test.log", stream);
        await model.SetTimestampPatternAsync(new TimestampPattern("12:00", 0));
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        await stats.CurrentCalculation;
        Assert.Equal("2", stats.Patterns[0].Values[0].Mean);
        var calculation = stats.CurrentCalculation;
        model.ShowFilteredOut = true;
        Assert.Same(calculation, stats.CurrentCalculation);
        stats.ScopeIndex = 1;
        await stats.CurrentCalculation;
        Assert.Equal("6", stats.Patterns[0].Values[0].Mean);
        stats.ScopeIndex = 0;
        await model.SetTimestampPatternAsync(null);
        await stats.CurrentCalculation;
        Assert.Equal(0, stats.Patterns[0].Values[0].Count);
        await model.ClearFiltersAsync();
        await stats.CurrentCalculation;
        Assert.Equal(2, stats.Patterns[0].Values[0].Count);
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("x=42"));
        await model.LoadAsync("new.log", second);
        await stats.CurrentCalculation;
        Assert.Equal("42", stats.Patterns[0].Values[0].Mean);
    }

    [Fact]
    public async Task StaleResultsAreDiscardedEvenIfCalculatorIgnoresCancellation()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var stats = new StatisticsViewModel(calculate: (pattern, lines, token) =>
        {
            if (lines[0].Text == "x=1") { started.Set(); release.Wait(TestContext.Current.CancellationToken); }
            return StatisticsCalculator.Calculate(pattern, lines, TestContext.Current.CancellationToken);
        });
        stats.SavePattern(StatisticsTests.Pattern());
        stats.SetSources(StatisticsTests.Lines("x=1"), StatisticsTests.Lines("x=1"), true);
        var old = stats.CurrentCalculation;
        Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        stats.SetSources(StatisticsTests.Lines("x=9"), StatisticsTests.Lines("x=9"), true);
        await stats.CurrentCalculation;
        release.Set();
        await old;
        Assert.Equal("9", stats.Patterns[0].Values[0].Mean);
    }

    [Fact]
    public async Task OnePatternErrorDoesNotStopOtherPatterns()
    {
        using var stats = new StatisticsViewModel();
        stats.SavePattern(StatisticsTests.Pattern(@"(?<x>(a+)+)$"));
        stats.SavePattern(StatisticsTests.Pattern());
        var lines = StatisticsTests.Lines(new string('a', 20000) + "!", "x=4");
        stats.SetSources(lines, lines, true);
        await stats.CurrentCalculation;
        Assert.Contains("100", stats.Patterns[0].Status);
        Assert.Empty(stats.Patterns[0].Values);
        Assert.Equal("4", stats.Patterns[1].Values[0].Mean);
    }
}
