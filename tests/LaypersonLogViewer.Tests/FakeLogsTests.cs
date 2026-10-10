using LaypersonLogViewer.FakeLogs;

namespace LaypersonLogViewer.Tests;

public sealed class FakeLogsTests
{
    [Fact]
    public void DefaultsAndExplicitOptions()
    {
        Assert.Equal(new GeneratorOptions(), GeneratorOptions.Parse([]));
        Assert.Equal(new GeneratorOptions(1, 12, -7, "file with spaces.log"),
            GeneratorOptions.Parse(["--interval-ms", "1", "--count", "12", "--seed", "-7", "--file", "file with spaces.log"]));
        Assert.True(GeneratorOptions.Parse(["--help"]).Help);
    }

    [Theory]
    [InlineData("--count", "0")]
    [InlineData("--count", "-1")]
    [InlineData("--interval-ms", "0")]
    [InlineData("--interval-ms", "1.5")]
    [InlineData("--seed", "abc")]
    [InlineData("--count", "2147483648")]
    [InlineData("--file", " ")]
    [InlineData("--unknown", "2")]
    [InlineData("--count", "--help")]
    public void InvalidOptionsAreRejected(string key, string value) =>
        Assert.ThrowsAny<ArgumentException>(() => GeneratorOptions.Parse([key, value]));

    [Fact]
    public void MissingAndRepeatedOptionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => GeneratorOptions.Parse(["--file"]));
        Assert.Throws<ArgumentException>(() => GeneratorOptions.Parse(["--count", "1", "--count", "2"]));
    }

    [Fact]
    public void SeedIsRepeatableAndContentExercisesViewerFeatures()
    {
        var timestamp = new DateTimeOffset(2026, 10, 10, 14, 30, 0, TimeSpan.Zero);
        var first = new LogGenerator(42);
        var second = new LogGenerator(42);
        var other = new LogGenerator(43);
        var entries = Enumerable.Range(1, 12).Select(i => first.Next(i, timestamp)).ToArray();
        Assert.Equal(entries, Enumerable.Range(1, 12).Select(i => second.Next(i, timestamp)));
        Assert.NotEqual(entries[0], other.Next(1, timestamp));
        foreach (var level in new[] { "INFO", "DEBUG", "WARN", "ERROR" })
            Assert.Contains(entries, e => e.Text.Contains(" " + level + " ", StringComparison.Ordinal));
        foreach (var category in new[] { "database", "network", "healthcheck" })
            Assert.Contains(entries, e => e.Text.Contains("category=" + category, StringComparison.Ordinal));
        Assert.Contains("Запрос выполнен", entries[0].Text);
        Assert.Contains("\n    at FakeClient.Send()", entries[3].Text);
        Assert.True(entries[9].Text.Length > 400);
        Assert.All(entries, e => Assert.Matches(@"duration=\d+\.\d size=\d+", e.Text));
    }

    [Fact]
    public async Task RoutesErrorsAndWritesExactEntryCountWithImmediateFlush()
    {
        using var output = new CountingWriter();
        using var errors = new CountingWriter();
        await LogGenerator.RunAsync(new(1, 12), output, errors, TestContext.Current.CancellationToken);
        Assert.Equal(9, output.Entries);
        Assert.Equal(3, errors.Entries);
        Assert.Equal(output.Entries, output.Flushes);
        Assert.Equal(errors.Entries, errors.Flushes);
        Assert.DoesNotContain(" ERROR ", output.ToString());
        Assert.Contains("request=12", errors.ToString());
        Assert.DoesNotContain("request=13", output.ToString() + errors);
    }

    [Fact]
    public async Task FileModeCreatesAppendsAndIncludesErrorsWithoutConsoleOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "FakeLogs-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var options = new GeneratorOptions(1, 4, File: path);
            await LogGenerator.RunAsync(options, output, error, TestContext.Current.CancellationToken);
            var original = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            await LogGenerator.RunAsync(options, output, error, TestContext.Current.CancellationToken);
            var appended = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.StartsWith(original, appended);
            Assert.Equal(8, appended.Split('\n').Count(line => line.Contains("request=", StringComparison.Ordinal)));
            Assert.Contains("ERROR", appended);
            Assert.Contains("Запрос выполнен", appended);
            Assert.Empty(output.ToString());
            Assert.Empty(error.ToString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task CancellationInterruptsDelayAfterACompleteFlushedEntry()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var output = new CountingWriter { OnFlush = stop.Cancel };
        using var errors = new StringWriter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LogGenerator.RunAsync(new(60000), output, errors, stop.Token));
        Assert.Equal(1, output.Entries);
        Assert.Equal(1, output.Flushes);
        Assert.EndsWith(output.NewLine, output.ToString());
    }

    private sealed class CountingWriter : StringWriter
    {
        public int Entries { get; private set; }
        public int Flushes { get; private set; }
        public Action? OnFlush { get; init; }
        public override Task WriteLineAsync(string? value) { Entries++; return base.WriteLineAsync(value); }
        public override Task FlushAsync() { Flushes++; OnFlush?.Invoke(); return Task.CompletedTask; }
    }
}
