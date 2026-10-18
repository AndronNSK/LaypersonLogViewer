using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class LogSearchTests
{
    private static LogSearchMatch? Find(IReadOnlyList<LogLine> lines, string text,
        bool caseSensitive = false, LogSearchMatch? current = null, bool backwards = false) =>
        LogSearch.Find(lines, text, caseSensitive, current, backwards, TestContext.Current.CancellationToken);

    [Fact]
    public void NavigationVisitsOccurrencesInBothDirectionsAndWraps()
    {
        var lines = StatisticsTests.Lines("x x", "nothing", "x");
        var first = Find(lines, "x")!;
        Assert.Equal((1, 0, false), (first.Line.Number, first.Start, first.Wrapped));
        var second = Find(lines, "x", current: first)!;
        Assert.Equal((1, 2), (second.Line.Number, second.Start));
        var third = Find(lines, "x", current: second)!;
        Assert.Equal(3, third.Line.Number);
        var wrapped = Find(lines, "x", current: third)!;
        Assert.Equal((1, 0, true), (wrapped.Line.Number, wrapped.Start, wrapped.Wrapped));
        var previous = Find(lines, "x", current: wrapped, backwards: true)!;
        Assert.Equal((3, 0, true), (previous.Line.Number, previous.Start, previous.Wrapped));
        Assert.Equal(2, Find(lines, "x", current: previous, backwards: true)!.Start);
        Assert.Equal(3, Find(lines, "x", backwards: true)!.Line.Number);
    }

    [Theory]
    [InlineData(".*", false, 1, 1)]
    [InlineData(" ошибка ", false, 2, 0)]
    [InlineData("ОШИБКА", true, 3, 0)]
    [InlineData("ОШИБКА", false, 2, 1)]
    [InlineData(" ", false, 2, 0)]
    public void SearchIsLiteralPreservesSpacesAndSupportsUnicodeAndCase(string query, bool sensitive, int number, int start)
    {
        var match = Find(StatisticsTests.Lines("a.*b", " ошибка ", "ОШИБКА"), query, sensitive)!;
        Assert.Equal(number, match.Line.Number);
        Assert.Equal(start, match.Start);
        Assert.Equal(query.Length, match.Length);
    }

    [Fact]
    public void EmptyMissingAndRemovedMatchesAreHandled()
    {
        var lines = StatisticsTests.Lines("x", "y");
        Assert.Null(Find([], "x"));
        Assert.Null(Find(lines, ""));
        Assert.Null(Find(lines, "z"));
        var current = Find(lines, "x")!;
        Assert.True(Find(lines, "x", current: current)!.Wrapped);
        Assert.Null(Find([lines[1]], "x", current: current));
        var replacement = StatisticsTests.Lines("new x");
        Assert.Equal(4, Find(replacement, "x", current: current)!.Start);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => LogSearch.Find(lines, "x", cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task ViewModelSearchUsesDisplayedScopeAndTracksLiveChanges()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        using var search = model.Search;
        var lines = StatisticsTests.Lines("INFO x", "ERROR x", "WARN x");
        await model.ApplyLiveLinesAsync("test", lines);
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        search.Text = "x";
        Assert.Equal(2, (await search.FindAsync())!.Line.Number);
        model.ShowFilteredOut = true;
        Assert.Equal(3, (await search.FindAsync())!.Line.Number);
        Assert.Equal(1, (await search.FindAsync())!.Line.Number);
        Assert.Contains("Поиск по кругу", search.Status);
        await model.ApplyLiveLinesAsync("test", [lines[1], lines[2], new(4, "new x")]);
        Assert.Equal(2, (await search.FindAsync())!.Line.Number);
        model.ShowFilteredOut = false;
        search.Text = "X";
        search.CaseSensitive = true;
        Assert.Null(await search.FindAsync());
        Assert.Equal("Не найдено", search.Status);
        search.CaseSensitive = false;
        Assert.Equal(2, (await search.FindAsync())!.Line.Number);
        await model.LoadAsync("empty", new MemoryStream());
        Assert.Null(await search.FindAsync());
    }

    [Theory]
    [InlineData("query")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task SupersededSearchAndClosingCannotApplyStaleResults(string change)
    {
        using var search = new LogSearchViewModel();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        search.SetLines(new PausingLines(StatisticsTests.Lines("old", "new"), () =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }));
        search.IsVisible = true;
        search.Text = "old";
        var old = search.FindAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            if (change == "query") search.Text = "new";
            else if (change == "source") search.SetLines(StatisticsTests.Lines("new"));
            else search.IsVisible = false;
        }
        finally { release.Set(); }
        Assert.Null(await old);
        Assert.False(search.IsSearching);
        Assert.DoesNotContain("Поиск…", search.Status);
        if (change == "query")
        {
            Assert.Equal(2, (await search.FindAsync())!.Line.Number);
            Assert.Equal("Строка 2", search.Status);
        }
    }

    private sealed class PausingLines(IReadOnlyList<LogLine> lines, Action pause) : IReadOnlyList<LogLine>
    {
        private int _reads;
        public int Count => lines.Count;
        public LogLine this[int index]
        {
            get { if (Interlocked.Increment(ref _reads) == 1) pause(); return lines[index]; }
        }
        public IEnumerator<LogLine> GetEnumerator() => lines.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
