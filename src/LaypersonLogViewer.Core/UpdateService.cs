using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace LaypersonLogViewer.Core;

public sealed record AvailableUpdate(Version Version, string FileName, Uri DownloadUrl, long Size, string Sha256);

public sealed class UpdateService(HttpClient client)
{
    public async Task<AvailableUpdate?> CheckAsync(Version current, string platform, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/AndronNSK/LaypersonLogViewer/releases/latest");
        request.Headers.UserAgent.ParseAdd("LaypersonLogViewer/" + current);
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<Release>(token);
        if (release is null || release.Assets is null || string.IsNullOrWhiteSpace(release.Tag) || release.Draft || release.Prerelease
            || !Version.TryParse(release.Tag.TrimStart('v'), out var version) || version <= current) return null;
        var expected = platform switch
        {
            "win-x64" => $"LaypersonLogViewer-{version}-win-x64-ru-ru.msi",
            "linux-x64" => $"layperson-log-viewer_{version}_ubuntu22.04_amd64.deb",
            _ => ""
        };
        var asset = release.Assets.FirstOrDefault(asset => asset.Name == expected && asset.State == "uploaded");
        if (asset is null || asset.Size <= 0 || asset.Digest is not { Length: 71 }
            || !asset.Digest.StartsWith("sha256:", StringComparison.Ordinal)
            || !asset.Digest[7..].All(Uri.IsHexDigit)) return null;
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var url) || url.Scheme != "https"
            || url.Host != "github.com" || !url.AbsolutePath.StartsWith(
                "/AndronNSK/LaypersonLogViewer/releases/download/", StringComparison.Ordinal)) return null;
        return new(version, expected, url, asset.Size, asset.Digest[7..]);
    }

    public async Task<string?> DownloadAsync(AvailableUpdate update, string directory,
        Func<AvailableUpdate, Task<bool>> confirm, CancellationToken token)
    {
        if (!await confirm(update)) return null;
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
        var destination = Path.Combine(directory, Path.GetFileName(update.FileName));
        try
        {
            using var response = await client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var output = File.Create(temporary))
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > update.Size) throw new InvalidDataException("Размер обновления превышает ожидаемый.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if (total != update.Size) throw new InvalidDataException("Обновление загружено не полностью.");
            }
            await using var file = File.OpenRead(temporary);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            if (!hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма обновления не совпадает.");
            file.Close();
            File.Move(temporary, destination, true);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record Release(
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] Asset[] Assets);
    private sealed record Asset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string Url,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("state")] string State);
}
