using System.Text.Json;

namespace LaypersonLogViewer.Core;

public sealed record StatisticsSettings(int FormatVersion, StatisticsScope Scope, IReadOnlyList<StatisticsPattern> Patterns)
{
    public static StatisticsSettings Empty => new(1, StatisticsScope.Filtered, []);
}
public interface IStatisticsSettingsStore
{
    Task<StatisticsSettings> LoadAsync();
    Task SaveAsync(StatisticsSettings settings);
}

public sealed class StatisticsSettingsStore(string path) : IStatisticsSettingsStore
{
    private bool _corrupt;
    public async Task<StatisticsSettings> LoadAsync()
    {
        if (!File.Exists(path)) return StatisticsSettings.Empty;
        try
        {
            var settings = JsonSerializer.Deserialize<StatisticsSettings>(await File.ReadAllTextAsync(path))
                ?? throw new JsonException("Пустой файл настроек.");
            if (settings.FormatVersion != 1 || !Enum.IsDefined(settings.Scope) || settings.Patterns is null
                || settings.Patterns.Any(p => p is null) || settings.Patterns.Select(p => p.Id).Distinct().Count() != settings.Patterns.Count)
                throw new JsonException("Неподдерживаемый формат настроек статистики.");
            foreach (var pattern in settings.Patterns) pattern.Validate();
            _corrupt = false;
            return settings;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            _corrupt = true;
            throw new InvalidDataException("Не удалось прочитать шаблоны статистики. Исходный файл сохранён.", e);
        }
    }

    public async Task SaveAsync(StatisticsSettings settings)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $"statistics-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            if (_corrupt && File.Exists(path))
                File.Copy(path, path + $".corrupt-{Guid.NewGuid():N}");
            File.Move(temporary, path, true);
            _corrupt = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

// The default for directly constructed view models/windows; production opts into disk storage.
public sealed class MemoryStatisticsSettingsStore : IStatisticsSettingsStore
{
    private StatisticsSettings _settings = StatisticsSettings.Empty;
    public Task<StatisticsSettings> LoadAsync() => Task.FromResult(_settings);
    public Task SaveAsync(StatisticsSettings settings) { _settings = settings; return Task.CompletedTask; }
}
