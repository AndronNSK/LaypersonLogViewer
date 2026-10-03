using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class StatisticsEditorTests
{
    [Fact]
    public async Task MarkingsNamesAndManualEditsRoundTripWithoutChangingOriginal()
    {
        using var editor = new StatisticsEditorViewModel("time=12 size=3", StatisticsTests.Lines("time=20 size=4"));
        Assert.False(editor.CanSave);
        Assert.True(editor.AddRange(0, 5, StatisticsRangeKind.Anchor));
        Assert.True(editor.AddRange(5, 2, StatisticsRangeKind.Number, "Время"));
        Assert.True(editor.AddRange(13, 1, StatisticsRangeKind.Number, "Размер"));
        Assert.False(editor.AddRange(1, 3, StatisticsRangeKind.Anchor));
        Assert.False(editor.AddRange(7, 5, StatisticsRangeKind.Number, "bad"));
        editor.Name = "Requests";
        Assert.True(editor.CanSave);
        await editor.PreviewTask;
        Assert.Contains("Время: 20", editor.PreviewText);
        Assert.Contains("Размер: 4", editor.PreviewText);
        var original = editor.BuildPattern();
        using var reopened = new StatisticsEditorViewModel("", [], original);
        Assert.Equal(original.Ranges, reopened.BuildPattern().Ranges);
        reopened.SetManualRegex(true);
        reopened.Expression = @"elapsed=(?<value1>\d+) (ignored) (?<extra>\d+)";
        Assert.Equal("Время", reopened.Values.Single(v => v.GroupName == "value1").DisplayName);
        Assert.DoesNotContain(reopened.Values, v => v.GroupName == "value2");
        Assert.Equal(2, reopened.Values.Count);
        reopened.Values.Single(v => v.GroupName == "extra").DisplayName = "Дополнительно";
        Assert.True(reopened.CanSave);
        Assert.Equal(2, original.Values.Count);
        Assert.False(original.ManualRegex);
        reopened.Expression = "[";
        Assert.False(reopened.CanSave);
        reopened.SetManualRegex(false);
        Assert.True(reopened.CanSave);
        Assert.Equal(original.Expression, reopened.Expression);
        reopened.RemoveRange(reopened.Ranges[0]);
        Assert.False(reopened.CanSave);
    }

    [Fact]
    public async Task ManualEditorWithoutExampleAndEmptyPreview()
    {
        using var editor = new StatisticsEditorViewModel("", []);
        Assert.True(editor.ManualRegex);
        editor.Expression = @"(?<temperature>-?\d+)";
        Assert.True(editor.CanSave);
        await editor.PreviewTask;
        Assert.Contains("Совпадений", editor.PreviewText);
        editor.Values[0].DisplayName = " ";
        Assert.False(editor.CanSave);
    }
}
