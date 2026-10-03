using System.Globalization;
using System.Text.RegularExpressions;

namespace LaypersonLogViewer.Core;

public sealed record ValueStatistics(string GroupName, string DisplayName, int Count, int Skipped,
    double? Mean, double? Minimum, double? Maximum, double? Median);
public sealed record PatternStatistics(Guid PatternId, IReadOnlyList<ValueStatistics> Values, string? Error = null);
public sealed record StatisticsPreview(int? LineNumber, string? Text, IReadOnlyList<string> Values, string? Error = null);

public static class StatisticsCalculator
{
    private static readonly Regex Number = new(@"\A(?:" + StatisticsPatternBuilder.NumberExpression + @")\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    public static bool TryParseNumber(string text, out double value)
    {
        value = 0;
        var trimmed = text.Trim();
        return Number.IsMatch(trimmed)
            && double.TryParse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    public static PatternStatistics Calculate(StatisticsPattern pattern, IReadOnlyList<LogLine> lines,
        CancellationToken cancellationToken = default)
    {
        try
        {
            pattern.Validate();
            var regex = pattern.CreateRegex();
            var values = pattern.Values.Select(_ => new List<double>()).ToArray();
            var skipped = new int[values.Length];
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = regex.Match(line.Text); // Deliberately only the first match per line.
                if (!match.Success) continue;
                for (var i = 0; i < values.Length; i++)
                {
                    var group = match.Groups[pattern.Values[i].GroupName];
                    if (group.Success && TryParseNumber(group.Value, out var value)) values[i].Add(value);
                    else skipped[i]++;
                }
            }
            var result = new List<ValueStatistics>();
            for (var i = 0; i < values.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var data = values[i];
                data.Sort();
                var definition = pattern.Values[i];
                if (data.Count == 0)
                {
                    result.Add(new(definition.GroupName, definition.DisplayName, 0, skipped[i], null, null, null, null));
                    continue;
                }
                // Scale before summing to avoid overflow, with compensated summation for precision.
                var scale = Math.Max(Math.Abs(data[0]), Math.Abs(data[^1]));
                double sum = 0, correction = 0;
                foreach (var value in data)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var term = scale == 0 ? 0 : value / scale - correction;
                    var next = sum + term;
                    correction = (next - sum) - term;
                    sum = next;
                }
                var mean = Math.Clamp(sum / data.Count, -1, 1) * scale;
                var middle = data.Count / 2;
                var median = data.Count % 2 == 1 ? data[middle] : Midpoint(data[middle - 1], data[middle]);
                result.Add(new(definition.GroupName, definition.DisplayName, data.Count, skipped[i],
                    mean, data[0], data[^1], median));
            }
            return new(pattern.Id, result);
        }
        catch (RegexMatchTimeoutException) { return new(pattern.Id, [], "Regex превысил лимит 100 мс. Уточните шаблон."); }
        catch (ArgumentException e) { return new(pattern.Id, [], e.Message); }
    }

    private static double Midpoint(double left, double right) => Math.Sign(left) == Math.Sign(right)
        ? left + (right - left) / 2 : left / 2 + right / 2;

    public static StatisticsPreview Preview(StatisticsPattern pattern, IReadOnlyList<LogLine> lines,
        CancellationToken cancellationToken = default)
    {
        try
        {
            pattern.Validate();
            var regex = pattern.CreateRegex();
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = regex.Match(line.Text);
                if (!match.Success) continue;
                var values = pattern.Values.Select(value =>
                {
                    var capture = match.Groups[value.GroupName];
                    return $"{value.DisplayName}: " + (capture.Success && TryParseNumber(capture.Value, out var number)
                        ? number.ToString("G17", CultureInfo.CurrentCulture) : "пропущено (не число)");
                }).ToArray();
                return new(line.Number, line.Text, values);
            }
            return new(null, null, []);
        }
        catch (RegexMatchTimeoutException) { return new(null, null, [], "Regex превысил лимит 100 мс."); }
        catch (ArgumentException e) { return new(null, null, [], e.Message); }
    }
}
