using System.Text;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class EntryViewModelTests
{
    private static readonly TimestampPattern Pattern = new("2026-10-03 12:34:56", 0);
    private static async Task Load(MainWindowViewModel model, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await model.LoadAsync("test.log", stream);
    }

    [Fact]
    public async Task GroupingChangesFilterScopeAndResetRestoresLineMode()
    {
        var model = new MainWindowViewModel();
        await Load(model, "2026-10-03 12:34:56 ERROR\n stack\n2026-10-03 12:34:57 INFO\n details");
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        Assert.Single(model.DisplayLines);
        Assert.Equal(2, await model.PreviewTimestampPatternAsync(Pattern));
        Assert.False(model.HasTimestampPattern);
        await model.SetTimestampPatternAsync(Pattern);
        Assert.Equal(2, model.EntryCount);
        Assert.Equal(1, model.MatchingEntryCount);
        Assert.Equal(new[] { 1, 2 }, model.DisplayLines.Select(row => row.Number));
        Assert.Equal(new[] { true, false }, model.DisplayLines.Select(row => row.IsEntryStart));
        model.ShowFilteredOut = true;
        Assert.Equal(new[] { false, false, true, true }, model.DisplayLines.Select(row => row.IsFilteredOut));
        await model.SetTimestampPatternAsync(null);
        Assert.False(model.HasTimestampPattern);
        Assert.True(model.ShowFilteredOut);
        Assert.Single(model.Filters);
        Assert.Equal(new[] { false, true, true, true }, model.DisplayLines.Select(row => row.IsFilteredOut));
        Assert.All(model.DisplayLines, row => Assert.False(row.IsEntryStart));
    }

    [Fact]
    public async Task ExclusionOnContinuationDimsEntireEntryAndRemovingItRestoresEntry()
    {
        var model = new MainWindowViewModel { ShowFilteredOut = true };
        await Load(model, "2026-10-03 12:34:56 ERROR\nignore");
        await model.SetTimestampPatternAsync(Pattern);
        var filter = new LogFilter(FilterKind.Exclude, "ignore");
        await model.AddFilterAsync(filter);
        Assert.All(model.DisplayLines, row => Assert.True(row.IsFilteredOut));
        Assert.Equal(0, model.MatchingEntryCount);
        await model.RemoveFilterAsync(filter);
        Assert.All(model.DisplayLines, row => Assert.False(row.IsFilteredOut));
        Assert.Equal(1, model.MatchingEntryCount);
    }

    [Fact]
    public async Task PatternSurvivesFileChangesAndNoMatchWarningCanBeCleared()
    {
        var model = new MainWindowViewModel();
        await model.SetTimestampPatternAsync(Pattern);
        Assert.False(model.HasGroupingWarning);
        await Load(model, "preamble\n2026-10-03 12:34:56 INFO\n detail");
        Assert.Equal(2, model.EntryCount);
        Assert.Equal(1, model.TimestampStartCount);
        await Load(model, "no timestamp\nmore text");
        Assert.Same(Pattern, model.TimestampPattern);
        Assert.Equal(1, model.EntryCount);
        Assert.True(model.HasGroupingWarning);
        await model.SetTimestampPatternAsync(null);
        Assert.False(model.HasGroupingWarning);
        Assert.Equal(2, model.EntryCount);
    }

    [Fact]
    public async Task FailedReadKeepsGroupingAndChangingPatternRebuildsEntries()
    {
        var model = new MainWindowViewModel();
        await Load(model, "2026-10-03 12:34:56 ERROR\n detail\n2026-10-03 12:34:57 INFO");
        await model.SetTimestampPatternAsync(Pattern);
        using var invalid = new MemoryStream(new byte[] { 0xff, 0xff, 0xff });
        await model.LoadAsync("invalid.log", invalid);
        Assert.True(model.HasError);
        Assert.Same(Pattern, model.TimestampPattern);
        Assert.Equal(2, model.EntryCount);
        await model.SetTimestampPatternAsync(new TimestampPattern("[12:34:56]", 0));
        Assert.Equal(1, model.EntryCount);
        Assert.True(model.HasGroupingWarning);
        Assert.Equal(3, model.DisplayLines.Count);
    }
}
