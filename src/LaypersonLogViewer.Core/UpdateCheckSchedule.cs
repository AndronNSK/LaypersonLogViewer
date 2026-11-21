using System.Text.Json;

namespace LaypersonLogViewer.Core;

public sealed record UpdateCheckState(DateTimeOffset? LastCheck = null, DateTimeOffset? NextAutomaticCheck = null,
    DateTimeOffset? RetryAfter = null);
public sealed record UpdateCheckResult(AvailableUpdate? Update, bool Checked, DateTimeOffset? RetryAfter = null);

public sealed class UpdateCheckSchedule(string path, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<UpdateCheckResult> CheckAsync(UpdateService service, Version current, string platform,
        bool manual, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Serialize simultaneous launches while reading and reserving the persisted schedule.
        FileStream lease;
        try { lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return new(null, false, _time.GetUtcNow().AddMinutes(1)); }
        await using (lease)
        {
            UpdateCheckState state;
            try
            {
                state = File.Exists(path)
                    ? JsonSerializer.Deserialize<UpdateCheckState>(await File.ReadAllBytesAsync(path, token)) ?? new()
                    : new();
            }
            catch (JsonException) { state = new(); }
            var now = _time.GetUtcNow();
            if (state.RetryAfter > now) return new(null, false, state.RetryAfter);
            if (!manual && state.NextAutomaticCheck > now) return new(null, false);
            state = new(now, now.AddDays(1));
            await SaveAsync(state);
            try { return new(await service.CheckAsync(current, platform, token), true); }
            catch (UpdateRateLimitException error)
            {
                await SaveAsync(state with { RetryAfter = error.RetryAt });
                throw;
            }
            catch (Exception)
            {
                await SaveAsync(state with { NextAutomaticCheck = now.AddHours(1) });
                throw;
            }
        }
    }

    public async Task DeferAsync(DateTimeOffset retryAt)
    {
        // Downloads use the same persisted backoff as discovery.
        await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var state = File.Exists(path) ? JsonSerializer.Deserialize<UpdateCheckState>(await File.ReadAllBytesAsync(path)) : null;
        state ??= new();
        await SaveAsync(state with { RetryAfter = state.RetryAfter > retryAt ? state.RetryAfter : retryAt });
    }

    private Task SaveAsync(UpdateCheckState state) => PatternFiles.WriteFileAsync(path, JsonSerializer.SerializeToUtf8Bytes(state));
}
