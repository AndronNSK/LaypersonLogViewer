using System.Text.Json;

namespace LaypersonLogViewer.Core;

// Separate document shapes prevent accidentally loading statistics as filters (or vice versa).
public static class PatternFiles
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static async Task WriteFileAsync(string path, byte[] contents)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"patterns-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, contents);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public sealed record Condition(string Text, bool CaseSensitive);
    public sealed record Filter(FilterKind Kind, Condition Condition, Condition[] AdditionalConditions);
    public sealed record FilterDocument(int FormatVersion, Filter[] Filters);

    public static Task SaveFiltersAsync(Stream stream, IEnumerable<LogFilter> filters) =>
        JsonSerializer.SerializeAsync(stream, new FilterDocument(1, filters.Select(f => new Filter(
            f.Kind, new(f.Text, f.CaseSensitive),
            f.AdditionalConditions.Select(c => new Condition(c.Text, c.CaseSensitive)).ToArray())).ToArray()), Options);

    public static async Task<LogFilter[]> LoadFiltersAsync(Stream stream)
    {
        try
        {
            var document = await JsonSerializer.DeserializeAsync<FilterDocument>(stream);
            if (document is null || document.FormatVersion != 1 || document.Filters is null)
                throw new ArgumentException("Неподдерживаемый формат фильтров.");
            return document.Filters.Select(f =>
            {
                if (f is null || f.Condition is null || f.AdditionalConditions is null
                    || f.AdditionalConditions.Any(c => c is null))
                    throw new ArgumentException("Неполное описание фильтра.");
                return new LogFilter(f.Kind, new LogFilterCondition(f.Condition.Text, f.Condition.CaseSensitive),
                    f.AdditionalConditions.Select(c => new LogFilterCondition(c.Text, c.CaseSensitive)));
            }).ToArray();
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { throw new InvalidDataException("Не удалось прочитать фильтры: " + e.Message, e); }
    }

    public static Task SaveStatisticsAsync(Stream stream, StatisticsSettings settings)
    {
        ValidateStatistics(settings);
        return JsonSerializer.SerializeAsync(stream, settings, Options);
    }

    public static async Task<StatisticsSettings> LoadStatisticsAsync(Stream stream)
    {
        try
        {
            var settings = await JsonSerializer.DeserializeAsync<StatisticsSettings>(stream);
            ValidateStatistics(settings);
            return settings!;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        { throw new InvalidDataException("Не удалось прочитать шаблоны статистики: " + e.Message, e); }
    }

    public static void ValidateStatistics(StatisticsSettings? settings)
    {
        if (settings is null || settings.FormatVersion != 1 || !Enum.IsDefined(settings.Scope)
            || settings.Patterns is null || settings.Patterns.Any(p => p is null)
            || settings.Patterns.Select(p => p.Id).Distinct().Count() != settings.Patterns.Count)
            throw new ArgumentException("Неподдерживаемый формат настроек статистики.");
        foreach (var pattern in settings.Patterns) pattern.Validate();
    }
}
