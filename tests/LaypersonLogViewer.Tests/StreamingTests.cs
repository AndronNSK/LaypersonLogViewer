using System.Collections.Concurrent;
using System.Text;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task IncomingBatchDoesNotToggleBusyOrRefreshToolbarBindings()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        var notifications = new List<string?>();
        model.PropertyChanged += (_, e) =>
        {
            Assert.False(model.IsBusy);
            Assert.DoesNotContain("Обработка", model.Status);
            notifications.Add(e.PropertyName);
        };
        Assert.True(await model.ApplyLiveLinesAsync("stream", [new(1, "first")]));
        Assert.DoesNotContain("", notifications);
        Assert.DoesNotContain(nameof(model.IsBusy), notifications);
        Assert.Contains(nameof(model.Status), notifications);
        Assert.Single(model.DisplayLines);
    }

    [Fact]
    public async Task SupersededSourceCannotApplyItsPendingBatch()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        await model.ApplyLiveLinesAsync("current", [new(1, "keep")]);
        Assert.False(await model.ApplyLiveLinesAsync("old", [new(2, "discard")], () => false));
        Assert.Equal("current", model.FileName);
        Assert.Equal("keep", Assert.Single(model.DisplayLines).Text);
    }

    [Fact]
    public async Task IncomingBatchesKeepCollectionAndExistingRowsWithoutReset()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        var first = new LogLine(1, "first");
        var second = new LogLine(2, "second");
        await model.ApplyLiveLinesAsync("test", [first, second]);
        var collection = model.DisplayLines;
        var row = collection[1];
        var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        ((System.Collections.Specialized.INotifyCollectionChanged)collection).CollectionChanged += (_, e) => actions.Add(e.Action);
        var third = new LogLine(3, "third");
        await model.ApplyLiveLinesAsync("test", [first, second, third]);
        Assert.Same(collection, model.DisplayLines);
        Assert.Same(row, model.DisplayLines[1]);
        Assert.Equal(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Add }, actions);
        actions.Clear();
        await model.ApplyLiveLinesAsync("test", [second, third]);
        Assert.Same(row, model.DisplayLines[0]);
        Assert.Equal(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Remove }, actions);
        model.ShowFilteredOut = true;
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "third"));
        Assert.Same(row, model.DisplayLines[0]);
        Assert.True(row.IsFilteredOut);
    }

    [Fact]
    public void BufferRetainsNewestLinesWithStableNumbersAndIndependentSnapshots()
    {
        var buffer = new LiveLogBuffer(200);
        for (var i = 0; i < 250; i++) buffer.Add("x=" + i);
        var before = buffer.Snapshot();
        Assert.Equal(200, before.Lines.Length);
        Assert.Equal(51, before.Lines[0].Number);
        buffer.Resize(100);
        var after = buffer.Snapshot();
        Assert.Equal(100, after.Lines.Length);
        Assert.Equal(151, after.Lines[0].Number);
        Assert.Equal(250, after.Received);
        Assert.Equal(200, before.Lines.Length);
        Assert.True(after.Revision > before.Revision);
    }

    [Fact]
    public async Task StandardInputReadsUnicodeMixedNewlinesAndFinalPartialLine()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Привет\r\n\nx=2\rпоследняя")).ToArray());
        var lines = new List<string>();
        await LiveLogSources.ReadAsync(stream, lines.Add, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Привет", "", "x=2", "последняя" }, lines);
    }

    [Fact]
    public async Task PauseKeepsCapturingThenRefreshesOnlyRetainedDataAndStatistics()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        using var live = model.Live;
        live.RetainedLines = 100;
        var started = new TaskCompletionSource<Action<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        live.Start("test stream", async (receive, token) =>
        {
            receive("ERROR x=1");
            started.SetResult(receive);
            await complete.Task.WaitAsync(token);
            return "EOF";
        });
        var write = await started.Task;
        stats.SavePattern(StatisticsTests.Pattern());
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
        Assert.True(await live.RefreshAsync(model));
        live.IsPaused = true;
        for (var i = 0; i < 150; i++) write(i % 2 == 0 ? "ERROR x=2" : "INFO x=9");
        Assert.False(await live.RefreshAsync(model));
        Assert.Single(model.AllLines);
        Assert.Equal(100, live.Snapshot().Length);
        live.IsPaused = false;
        Assert.True(await live.RefreshAsync(model));
        await stats.CurrentCalculation;
        Assert.Equal(50, model.VisibleLines.Count);
        Assert.Equal("2", stats.Patterns[0].Values[0].Mean);
        var calculation = stats.CurrentCalculation;
        model.ShowFilteredOut = true;
        Assert.False(await live.RefreshAsync(model));
        Assert.Same(calculation, stats.CurrentCalculation);
        complete.SetResult();
        await live.Completion;
        await live.RefreshAsync(model);
        Assert.False(live.IsActive);
        Assert.Contains("EOF", live.Status);
        Assert.Equal(100, model.DisplayLines.Count);
    }

    [Fact]
    public async Task SwitchingSourcesRejectsLateLinesFromPreviousSourceAndStopRetainsData()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        using var live = model.Live;
        var oldWriter = new TaskCompletionSource<Action<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        live.Start("old", async (receive, token) =>
        {
            oldWriter.SetResult(receive);
            await Task.Delay(Timeout.Infinite, token);
            return "old EOF";
        });
        var write = await oldWriter.Task;
        var oldCompletion = live.Completion;
        live.Start("new", (receive, _) => { receive("new"); return Task.FromResult("done"); });
        write("late old line");
        await live.Completion;
        await oldCompletion;
        await live.RefreshAsync(model);
        Assert.Equal("new", Assert.Single(model.AllLines).Text);
        live.Stop();
        Assert.Equal("new", Assert.Single(live.Snapshot()).Text);
    }

    [Fact]
    public async Task MultilineEntryIsReevaluatedWhenContinuationArrives()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        await model.SetTimestampPatternAsync(new TimestampPattern("12:00", 0));
        await model.AddFilterAsync(new LogFilter(FilterKind.Include, new("ERROR"), [new("database")]));
        await model.ApplyLiveLinesAsync("stream", [new(1, "12:00 ERROR")]);
        Assert.Empty(model.VisibleLines);
        await model.ApplyLiveLinesAsync("stream", [new(1, "12:00 ERROR"), new(2, "database failed")]);
        Assert.Equal(2, model.VisibleLines.Count);
    }

    [Fact]
    public async Task FileFollowingHandlesSplitUtf8CrLfTruncationAndRotation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LiveLogTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.log");
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var received = new ConcurrentQueue<string>();
        await File.WriteAllTextAsync(path, "first\n", stop.Token);
        var follow = LiveLogSources.FollowFileAsync(path, received.Enqueue, stop.Token, TimeSpan.FromMilliseconds(10));
        try
        {
            await Until(() => received.Count == 1, stop.Token);
            var bytes = Encoding.UTF8.GetBytes("Я\r\n");
            await using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                await writer.WriteAsync(bytes.AsMemory(0, 1), stop.Token);
                await writer.FlushAsync(stop.Token);
                await Task.Delay(60, stop.Token);
                Assert.Single(received);
                await writer.WriteAsync(bytes.AsMemory(1, 2), stop.Token);
                await writer.FlushAsync(stop.Token);
                await Until(() => received.Count == 2, stop.Token);
                await writer.WriteAsync(bytes.AsMemory(3), stop.Token);
                await writer.FlushAsync(stop.Token);
            }
            await Task.Delay(60, stop.Token);
            Assert.Equal(new[] { "first", "Я" }, received.ToArray());
            await File.WriteAllTextAsync(path, "b\n", stop.Token);
            await Until(() => received.Contains("b"), stop.Token);
            File.Move(path, path + ".old");
            await Task.Delay(60, stop.Token);
            await File.WriteAllTextAsync(path, "rotated\n", stop.Token);
            await Until(() => received.Contains("rotated"), stop.Token);
            await Task.Delay(60, stop.Token);
            Assert.Equal(new[] { "first", "Я", "b", "rotated" }, received.ToArray());
        }
        finally
        {
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follow);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task CommandCapturesBothStreamsAndExitCode()
    {
        var lines = new ConcurrentQueue<string>();
        var executable = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows() ? "/d /c \"echo output& echo error 1>&2& exit /b 7\""
            : "-c \"echo output; echo error >&2; exit 7\"";
        var code = await LiveLogSources.RunCommandAsync(executable, arguments, null, lines.Enqueue, TestContext.Current.CancellationToken);
        Assert.Equal(7, code);
        Assert.Contains(lines, line => line.Trim() == "output");
        Assert.Contains(lines, line => line.Trim() == "error");
    }

    [Fact]
    public async Task StopCancelsRunningCommand()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executable = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        var arguments = OperatingSystem.IsWindows() ? "-NoProfile -Command \"Write-Output ready; Start-Sleep 60\""
            : "-c \"echo ready; sleep 60\"";
        var command = LiveLogSources.RunCommandAsync(executable, arguments, null, _ => started.TrySetResult(), stop.Token);
        await started.Task.WaitAsync(stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedSourceReportsErrorWithoutDiscardingReceivedLines()
    {
        var model = new MainWindowViewModel();
        using var stats = model.Statistics;
        using var live = model.Live;
        live.Start("failure", (receive, _) => { receive("received"); throw new IOException("test failure"); });
        await live.Completion;
        await live.RefreshAsync(model);
        Assert.Contains("test failure", live.Status);
        Assert.Equal("received", Assert.Single(model.AllLines).Text);
    }

    private static async Task Until(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(10, token);
    }
}
