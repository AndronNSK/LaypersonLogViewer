using System.Text.RegularExpressions;

namespace LaypersonLogViewer.Core;

public enum StatisticsScope { Filtered, WholeFile }
public enum StatisticsRangeKind { Anchor, Number }
public sealed record StatisticsRange(int Start, int Length, StatisticsRangeKind Kind, string GroupName = "");
public sealed record StatisticsValue(string GroupName, string DisplayName);
public sealed record StatisticsPattern(Guid Id, string Name, string Expression, bool CaseSensitive,
    string Example, bool ManualRegex, IReadOnlyList<StatisticsRange> Ranges, IReadOnlyList<StatisticsValue> Values)
{
    public Regex CreateRegex() => new(Expression,
        RegexOptions.CultureInvariant | (CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
        TimeSpan.FromMilliseconds(100));

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(Expression);
        if (Example is null || Ranges is null || Values is null || Values.Any(v => v is null))
            throw new ArgumentException("Неполное описание шаблона.");
        var end = 0;
        foreach (var range in Ranges.OrderBy(r => r?.Start))
        {
            if (range is null || !Enum.IsDefined(range.Kind) || range.Start < end || range.Length <= 0
                || range.Start > Example.Length - range.Length)
                throw new ArgumentException("Некорректные фрагменты примера.");
            end = range.Start + range.Length;
        }
        var names = NamedGroups(CreateRegex());
        if (names.Length == 0) throw new ArgumentException("Добавьте хотя бы одну именованную группу: (?<value1>...).");
        if (!names.Order().SequenceEqual(Values.Select(v => v.GroupName).Order())
            || Values.Any(v => string.IsNullOrWhiteSpace(v.DisplayName)))
            throw new ArgumentException("У каждой именованной группы должно быть название.");
        if (!ManualRegex && StatisticsPatternBuilder.Build(Example, Ranges) != Expression)
            throw new ArgumentException("Regex не соответствует отмеченным фрагментам.");
    }

    public static string[] NamedGroups(Regex regex) => regex.GetGroupNames()
        .Where(name => !int.TryParse(name, out _)).ToArray();
}

public static class StatisticsPatternBuilder
{
    public const string NumberExpression = @"[+-]?(?:[0-9]+(?:[.,][0-9]+)?|[.,][0-9]+)(?:[eE][+-]?[0-9]+)?";

    public static string Build(string example, IEnumerable<StatisticsRange> ranges)
    {
        var ordered = ranges.OrderBy(r => r.Start).ToArray();
        if (!ordered.Any(r => r.Kind == StatisticsRangeKind.Anchor)
            || !ordered.Any(r => r.Kind == StatisticsRangeKind.Number))
            throw new ArgumentException("Отметьте постоянный текст и хотя бы одно числовое значение.");
        var parts = new List<string>();
        var names = new HashSet<string>();
        var end = 0;
        foreach (var range in ordered)
        {
            if (range.Start < end || range.Length <= 0 || range.Start < 0 || range.Start > example.Length - range.Length)
                throw new ArgumentException("Фрагменты не должны пересекаться или выходить за пределы строки.");
            if (parts.Count > 0 && range.Start > end) parts.Add(".*?");
            var text = example.Substring(range.Start, range.Length);
            if (range.Kind == StatisticsRangeKind.Anchor) parts.Add(Regex.Escape(text));
            else if (range.Kind == StatisticsRangeKind.Number)
            {
                if (!StatisticsCalculator.TryParseNumber(text, out _))
                    throw new ArgumentException("Выделите число целиком, без единиц измерения.");
                if (!Regex.IsMatch(range.GroupName, @"\A[A-Za-z][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(100)) || !names.Add(range.GroupName))
                    throw new ArgumentException("Имена групп должны быть уникальными и начинаться с латинской буквы.");
                parts.Add($"(?<{range.GroupName}>{NumberExpression})");
            }
            else throw new ArgumentException("Неизвестный тип фрагмента.");
            end = range.Start + range.Length;
        }
        return string.Concat(parts);
    }
}
