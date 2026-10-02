using System.Text;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class ViewModelTests
{
    private static async Task Load(MainWindowViewModel model, string text, string name = "test.log")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await model.LoadAsync(name, stream);
    }

    [Fact]
    public async Task AddRemoveAndClearRecalculateVisibleLines()
    {
        var model = new MainWindowViewModel();
        await Load(model, "INFO ready\nERROR server\nERROR local");
        var include = new LogFilter(FilterKind.Include, "ERROR");
        var exclude = new LogFilter(FilterKind.Exclude, "local");
        await model.AddFilterAsync(include);
        await model.AddFilterAsync(exclude);
        Assert.Equal(2, Assert.Single(model.VisibleLines).Number);
        await model.RemoveFilterAsync(exclude);
        Assert.Equal(2, model.VisibleLines.Count);
        await model.ClearFiltersAsync();
        Assert.Equal(3, model.VisibleLines.Count);
        Assert.True(model.HasNoFilters);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task LoadingNewFileReplacesLinesAndKeepsFilters()
    {
        var model = new MainWindowViewModel();
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "error"));
        await Load(model, "ERROR old\nINFO old");
        await Load(model, "INFO new\nERROR new", "new.log");
        Assert.Equal("new.log", model.FileName);
        Assert.Equal(new LogLine(2, "ERROR new"), Assert.Single(model.VisibleLines));
        Assert.Single(model.Filters);
    }

    [Fact]
    public async Task FailedLoadKeepsPreviousFileAndRecoversOnNextLoad()
    {
        var model = new MainWindowViewModel();
        await Load(model, "Original");
        using var brokenStream = new BrokenStream();
        await model.LoadAsync("broken.log", brokenStream);
        Assert.True(model.HasError);
        Assert.False(model.IsBusy);
        Assert.Equal("test.log", model.FileName);
        Assert.Equal("Original", Assert.Single(model.VisibleLines).Text);
        await Load(model, "Recovered");
        Assert.False(model.HasError);
        Assert.Equal("Recovered", Assert.Single(model.VisibleLines).Text);
    }

    [Fact]
    public async Task EmptyStatesDistinguishNoFileEmptyFileAndNoMatches()
    {
        var model = new MainWindowViewModel();
        Assert.Contains("Откройте", model.EmptyMessage);
        await Load(model, "");
        Assert.Equal("Файл пуст", model.EmptyMessage);
        await Load(model, "INFO");
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        Assert.True(model.IsEmpty);
        Assert.Contains("Нет строк", model.EmptyMessage);
    }

    [Fact]
    public async Task GreyModePreservesContextAndSwitchingBackRestoresMatches()
    {
        var model = new MainWindowViewModel();
        await Load(model, "INFO ready\nERROR server\nERROR local\nERROR server");
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        await model.AddFilterAsync(new LogFilter(FilterKind.Exclude, "local"));
        Assert.False(model.ShowFilteredOut);
        Assert.Equal(new[] { 2, 4 }, model.DisplayLines.Select(row => row.Number));

        model.ShowFilteredOut = true;
        Assert.Equal(new[] { 1, 2, 3, 4 }, model.DisplayLines.Select(row => row.Number));
        Assert.Equal(new[] { true, false, true, false }, model.DisplayLines.Select(row => row.IsFilteredOut));
        Assert.Contains("Показано 4 из 4", model.Status);
        Assert.Contains("Совпадений: 2", model.Status);

        model.ShowFilteredOut = false;
        Assert.Equal(new[] { 2, 4 }, model.DisplayLines.Select(row => row.Number));
        Assert.Equal(2, model.Filters.Count);
    }

    [Fact]
    public async Task GreyModeUpdatesAfterFilterChangesAndLoadingAnotherFile()
    {
        var model = new MainWindowViewModel { ShowFilteredOut = true };
        await Load(model, "INFO\nERROR");
        Assert.All(model.DisplayLines, row => Assert.False(row.IsFilteredOut));
        var filter = new LogFilter(FilterKind.Exclude, "ERROR");
        await model.AddFilterAsync(filter);
        Assert.True(model.DisplayLines[1].IsFilteredOut);
        await model.RemoveFilterAsync(filter);
        Assert.All(model.DisplayLines, row => Assert.False(row.IsFilteredOut));
        await model.AddFilterAsync(filter);
        await Load(model, "ERROR new\nINFO new", "new.log");
        Assert.True(model.ShowFilteredOut);
        Assert.Equal(new[] { true, false }, model.DisplayLines.Select(row => row.IsFilteredOut));
        await model.ClearFiltersAsync();
        Assert.All(model.DisplayLines, row => Assert.False(row.IsFilteredOut));
    }

    [Fact]
    public async Task GreyModeShowsNonmatchingLinesButStillRecognizesEmptyFiles()
    {
        var model = new MainWindowViewModel { ShowFilteredOut = true };
        Assert.True(model.IsEmpty);
        await Load(model, "INFO");
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        Assert.False(model.IsEmpty);
        Assert.True(Assert.Single(model.DisplayLines).IsFilteredOut);
        Assert.Contains("Совпадений: 0", model.Status);
        model.ShowFilteredOut = false;
        Assert.True(model.IsEmpty);
        model.ShowFilteredOut = true;
        await Load(model, "");
        Assert.True(model.IsEmpty);
        Assert.Equal("Файл пуст", model.EmptyMessage);
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Test read failure"));
    }
}
