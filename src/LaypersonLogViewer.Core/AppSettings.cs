using System.Text.Json;

namespace LaypersonLogViewer.Core;

public sealed record AppSettings(int LineLimit = LiveLogBuffer.DefaultCapacity, bool CheckForUpdates = true);

public sealed class AppSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaypersonLogViewer", "settings.json");

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(path)) return new();
        using var file = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(file) ?? throw new InvalidDataException("Пустые настройки.");
        if (settings.LineLimit is < LiveLogBuffer.MinimumCapacity or > LiveLogBuffer.MaximumCapacity)
            throw new InvalidDataException("Недопустимый лимит строк.");
        return settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await PatternFiles.WriteFileAsync(path, JsonSerializer.SerializeToUtf8Bytes(settings));
    }
}
