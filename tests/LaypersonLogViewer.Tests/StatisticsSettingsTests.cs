using System.Text.Json;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class StatisticsSettingsTests
{
    [Fact]
    public async Task DiskRoundTripAndCorruptFileBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LaypersonStatisticsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "statistics.json");
            var store = new StatisticsSettingsStore(path);
            Assert.Empty((await store.LoadAsync()).Patterns);
            var pattern = StatisticsTests.Pattern();
            var settings = new StatisticsSettings(1, StatisticsScope.WholeFile, [pattern]);
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();
            Assert.Equal(settings.Scope, loaded.Scope);
            Assert.Equal(pattern.Expression, loaded.Patterns[0].Expression);
            Assert.Equal(pattern.Values, loaded.Patterns[0].Values);
            Assert.DoesNotContain("Mean", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            await File.WriteAllTextAsync(path, "broken json", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(store.LoadAsync);
            Assert.Equal("broken json", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            await store.SaveAsync(settings);
            var backup = Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"));
            Assert.Equal("broken json", await File.ReadAllTextAsync(backup, TestContext.Current.CancellationToken));
            Assert.Single((await store.LoadAsync()).Patterns);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settings with { FormatVersion = 999 }), TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(store.LoadAsync);
        }
        finally { Directory.Delete(directory, true); }
    }
}
