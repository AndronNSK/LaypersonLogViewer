using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Globalization;

namespace LaypersonLogViewer.Core;

public sealed record AvailableUpdate(Version Version, string FileName, Uri DownloadUrl, long Size, string Sha256);

public sealed record UpdateManifest(int FormatVersion, string Version, UpdatePackage[] Packages);
public sealed record UpdatePackage(string Platform, string FileName, string DownloadUrl, long Size, string Sha256);
public sealed class UpdateRateLimitException(DateTimeOffset retryAt)
    : IOException($"Проверка обновлений временно ограничена. Повторить после {retryAt.LocalDateTime:g}.")
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

public sealed class UpdateService(HttpClient client, TimeProvider? timeProvider = null)
{
    public const string ManifestUrl = "https://github.com/AndronNSK/LaypersonLogViewer/releases/latest/download/updates.json";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<AvailableUpdate?> CheckAsync(Version current, string platform, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        request.Headers.UserAgent.ParseAdd("LaypersonLogViewer/" + current);
        using var response = await client.SendAsync(request, token);
        await CheckResponseAsync(response, token);
        var manifest = await response.Content.ReadFromJsonAsync<UpdateManifest>(token);
        return ReadManifest(manifest, current, platform);
    }

    public static AvailableUpdate? ReadManifest(UpdateManifest? manifest, Version current, string platform)
    {
        if (manifest is null || manifest.FormatVersion != 1 || manifest.Packages is null
            || !Version.TryParse(manifest.Version, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidDataException("Неподдерживаемый формат сведений об обновлении.");
        if (version <= current) return null;
        var expected = platform switch
        {
            "win-x64" => $"LaypersonLogViewer-{version}-win-x64-ru-ru.msi",
            "linux-x64" => $"layperson-log-viewer_{version}_ubuntu22.04_amd64.deb",
            _ => ""
        };
        var candidates = manifest.Packages.Where(asset => asset is not null && asset.Platform == platform).ToArray();
        if (candidates.Length > 1) throw new InvalidDataException("Повторяющаяся платформа в сведениях об обновлении.");
        if (expected.Length == 0 || candidates.Length == 0) return null;
        var asset = candidates[0];
        var expectedUrl = $"https://github.com/AndronNSK/LaypersonLogViewer/releases/download/{version}/{expected}";
        if (asset.FileName != expected || asset.Size <= 0 || asset.Sha256 is not { Length: 64 }
            || !asset.Sha256.All(Uri.IsHexDigit) || asset.DownloadUrl != expectedUrl)
            throw new InvalidDataException("Некорректные сведения об установщике.");
        return new(version, expected, new Uri(expectedUrl), asset.Size, asset.Sha256);
    }

    private async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new IOException("Сведения об обновлениях ещё не опубликованы.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values) && values.Contains("0");
            var body = await response.Content.ReadAsStringAsync(token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests || retry is not null || remaining
                || body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                var now = _time.GetUtcNow();
                var until = now.AddMinutes(1);
                var retryDate = retry?.Date ?? (retry?.Delta is { } delta ? now.Add(delta) : now);
                if (retryDate > until) until = retryDate;
                if (remaining && response.Headers.TryGetValues("X-RateLimit-Reset", out var reset)
                    && long.TryParse(reset.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                    && seconds is >= 0 and <= 253402300799)
                {
                    var resetDate = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    if (resetDate > until) until = resetDate;
                }
                throw new UpdateRateLimitException(until);
            }
        }
        response.EnsureSuccessStatusCode();
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
            await CheckResponseAsync(response, token);
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

}
