using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class UpdateScheduleTests
{
    private static readonly Version Current = new("0.2.7");
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            Assert.Equal(UpdateService.ManifestUrl, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(response());
        }
    }
    private sealed class StateFolder : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string PathName => Path.Combine(_directory, "update-check.json");
        public void Dispose()
        {
            if (!Directory.Exists(_directory)) return;
            foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }
    private static HttpResponseMessage NoUpdate() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new UpdateManifest(1, "0.2.7", [])))
    };

    [Fact]
    public async Task DailyScheduleSurvivesRestartsAndAllowsManualChecks()
    {
        using var folder = new StateFolder();
        var clock = new Clock();
        var handler = new Handler(NoUpdate);
        using var client = new HttpClient(handler);
        var service = new UpdateService(client, clock);
        async Task<UpdateCheckResult> Check(bool manual = false) =>
            await new UpdateCheckSchedule(folder.PathName, clock).CheckAsync(service, Current, "win-x64",
                manual, TestContext.Current.CancellationToken);

        Assert.True((await Check()).Checked);
        clock.Now = clock.Now.AddHours(23);
        Assert.False((await Check()).Checked);
        Assert.Equal(1, handler.Requests);
        Assert.True((await Check(true)).Checked);
        clock.Now = clock.Now.AddHours(23);
        Assert.False((await Check()).Checked);
        clock.Now = clock.Now.AddHours(1);
        Assert.True((await Check()).Checked);
        Assert.Equal(3, handler.Requests);
    }

    [Theory]
    [InlineData(403, true)]
    [InlineData(429, false)]
    public async Task ServerBackoffIsPersistedAndAlsoBlocksManualChecks(int status, bool useReset)
    {
        using var folder = new StateFolder();
        var clock = new Clock();
        var until = clock.Now.AddHours(2);
        var handler = new Handler(() =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("rate limit exceeded") };
            if (useReset)
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", until.ToUnixTimeSeconds().ToString());
            }
            else response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(2));
            return response;
        });
        using var client = new HttpClient(handler);
        var service = new UpdateService(client, clock);
        var schedule = new UpdateCheckSchedule(folder.PathName, clock);
        var error = await Assert.ThrowsAsync<UpdateRateLimitException>(() =>
            schedule.CheckAsync(service, Current, "win-x64", false, TestContext.Current.CancellationToken));
        Assert.Equal(until, error.RetryAt);
        var result = await new UpdateCheckSchedule(folder.PathName, clock)
            .CheckAsync(service, Current, "win-x64", true, TestContext.Current.CancellationToken);
        Assert.False(result.Checked);
        Assert.Equal(until, result.RetryAfter);
        Assert.Equal(1, handler.Requests);
        clock.Now = until;
        await Assert.ThrowsAsync<UpdateRateLimitException>(() =>
            schedule.CheckAsync(service, Current, "win-x64", true, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task UnavailableManifestIsRetriedAfterAnHourAcrossRestarts()
    {
        using var folder = new StateFolder();
        var clock = new Clock();
        var handler = new Handler(() => new(HttpStatusCode.NotFound) { Content = new StringContent("") });
        using var client = new HttpClient(handler);
        var service = new UpdateService(client, clock);
        await Assert.ThrowsAsync<IOException>(() => new UpdateCheckSchedule(folder.PathName, clock)
            .CheckAsync(service, Current, "win-x64", false, TestContext.Current.CancellationToken));
        Assert.False((await new UpdateCheckSchedule(folder.PathName, clock)
            .CheckAsync(service, Current, "win-x64", false, TestContext.Current.CancellationToken)).Checked);
        Assert.Equal(1, handler.Requests);
        clock.Now = clock.Now.AddHours(1);
        await Assert.ThrowsAsync<IOException>(() => new UpdateCheckSchedule(folder.PathName, clock)
            .CheckAsync(service, Current, "win-x64", false, TestContext.Current.CancellationToken));
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task SimultaneousChecksShareTheFileLease()
    {
        using var folder = new StateFolder();
        Directory.CreateDirectory(Path.GetDirectoryName(folder.PathName)!);
        await using var lease = new FileStream(folder.PathName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var handler = new Handler(NoUpdate);
        using var client = new HttpClient(handler);
        var result = await new UpdateCheckSchedule(folder.PathName).CheckAsync(
            new UpdateService(client), Current, "win-x64", true, TestContext.Current.CancellationToken);
        Assert.False(result.Checked);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("http://github.com/file", "abc")]
    [InlineData("https://example.com/setup.msi", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidManifestPackagesAreRejected(string url, string hash)
    {
        var manifest = new UpdateManifest(1, "0.3.0",
            [new("win-x64", "LaypersonLogViewer-0.3.0-win-x64-ru-ru.msi", url, 123, hash)]);
        Assert.Throws<InvalidDataException>(() => UpdateService.ReadManifest(manifest, Current, "win-x64"));
    }
}
