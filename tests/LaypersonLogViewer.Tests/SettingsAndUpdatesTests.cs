using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class SettingsAndUpdatesTests
{
    [Fact]
    public async Task SettingsPersistAndInvalidLimitsAreRejected()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "settings.json");
        var store = new AppSettingsStore(path);
        try
        {
            Assert.Equal(new AppSettings(), await store.LoadAsync());
            var settings = new AppSettings(12345, false);
            await store.SaveAsync(settings);
            Assert.Equal(settings, await store.LoadAsync());
            await File.WriteAllTextAsync(path, """{"LineLimit":99}""", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    [Theory]
    [InlineData("0.3.0", true)]
    [InlineData("0.2.6", false)]
    public async Task ChecksManifestForNewVersionsWithoutCallingRestApi(string version, bool available)
    {
        var handler = new FakeHandler(_ => new StringContent(JsonSerializer.Serialize(new
        {
            FormatVersion = 1, Version = version,
            Packages = new[] { new UpdatePackage("win-x64", $"LaypersonLogViewer-{version}-win-x64-ru-ru.msi",
                $"https://github.com/AndronNSK/LaypersonLogViewer/releases/download/{version}/LaypersonLogViewer-{version}-win-x64-ru-ru.msi",
                3, new string('a', 64)) }
        })));
        using var client = new HttpClient(handler);
        var update = await new UpdateService(client).CheckAsync(new Version("0.2.6"), "win-x64", TestContext.Current.CancellationToken);
        Assert.Equal(available, update is not null);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(UpdateService.ManifestUrl, handler.LastUrl);
    }

    [Fact]
    public async Task DownloadRequiresConfirmationAndVerifiesChecksumBeforeKeepingFile()
    {
        var bytes = Encoding.UTF8.GetBytes("fake installer");
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var handler = new FakeHandler(_ => new ByteArrayContent(bytes));
        using var client = new HttpClient(handler);
        var service = new UpdateService(client);
        var update = new AvailableUpdate(new Version("0.3.0"), "test.msi",
            new Uri("https://github.com/AndronNSK/LaypersonLogViewer/releases/download/0.3.0/test.msi"),
            bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
        try
        {
            Assert.Null(await service.DownloadAsync(update, folder, _ => Task.FromResult(false), TestContext.Current.CancellationToken));
            Assert.Equal(0, handler.Requests);
            Assert.False(Directory.Exists(folder));
            var path = await service.DownloadAsync(update, folder, _ => Task.FromResult(true), TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken));
            File.Delete(path!);
            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(
                update with { Sha256 = new string('0', 64) }, folder, _ => Task.FromResult(true), TestContext.Current.CancellationToken));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string? LastUrl { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            LastUrl = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) });
        }
    }
}
