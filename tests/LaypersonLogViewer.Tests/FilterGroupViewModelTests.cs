using System.Text;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FilterGroupViewModelTests
{
    [Theory]
    [InlineData(FilterKind.Include)]
    [InlineData(FilterKind.Exclude)]
    public async Task ChildrenCanBeAddedRemovedAndDeletedWithTheirParent(FilterKind kind)
    {
        var model = new MainWindowViewModel { ShowFilteredOut = true };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ERROR database\nERROR other\nINFO"));
        await model.LoadAsync("test.log", stream);
        await model.AddFilterAsync(new LogFilter(kind, "ERROR"));
        var condition = new LogFilterCondition("database", true);
        await model.AddConditionAsync(model.Filters[0], condition);
        var parent = Assert.Single(model.Filters);
        Assert.Same(condition, Assert.Single(parent.AdditionalConditions));
        Assert.Contains("Фильтров: 1", model.Status);
        Assert.Equal(kind == FilterKind.Include ? new[] { false, true, true } : new[] { true, false, false },
            model.DisplayLines.Select(row => row.IsFilteredOut));
        await model.RemoveConditionAsync(parent, condition);
        Assert.Empty(model.Filters[0].AdditionalConditions);
        Assert.Equal(kind == FilterKind.Include ? 2 : 1, model.VisibleLines.Count);
        await model.AddConditionAsync(model.Filters[0], condition);
        await model.RemoveFilterAsync(model.Filters[0]);
        Assert.Empty(model.Filters);
        Assert.Equal(3, model.VisibleLines.Count);
    }

    [Fact]
    public async Task GroupedEntryIsDimmedAsAWholeAndResetRestoresLineMatching()
    {
        var model = new MainWindowViewModel { ShowFilteredOut = true };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12:00 ERROR\ndatabase\n12:01 ERROR\nother"));
        await model.LoadAsync("test.log", stream);
        await model.SetTimestampPatternAsync(new TimestampPattern("12:00", 0));
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        await model.AddConditionAsync(model.Filters[0], new LogFilterCondition("database"));
        Assert.Equal(new[] { false, false, true, true }, model.DisplayLines.Select(row => row.IsFilteredOut));
        Assert.Equal(1, model.MatchingEntryCount);
        await model.SetTimestampPatternAsync(null);
        Assert.All(model.DisplayLines, row => Assert.True(row.IsFilteredOut));
        Assert.Single(model.Filters[0].AdditionalConditions);
    }
}
