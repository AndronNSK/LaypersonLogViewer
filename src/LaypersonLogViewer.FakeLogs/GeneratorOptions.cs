using System.Globalization;

namespace LaypersonLogViewer.FakeLogs;

public sealed record GeneratorOptions(int IntervalMs = 500, int? Count = null, int Seed = 42, string? File = null, bool Help = false)
{
    public static GeneratorOptions Parse(string[] args)
    {
        var result = new GeneratorOptions();
        var seen = new HashSet<string>();
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option)) throw new ArgumentException($"Повторный параметр: {option}");
            if (option == "--help") { result = result with { Help = true }; continue; }
            if (option is not ("--interval-ms" or "--count" or "--seed" or "--file"))
                throw new ArgumentException($"Неизвестный параметр: {option}");
            if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Не указано значение {option}");
            var value = args[index];
            if (option == "--file")
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                result = result with { File = value };
                continue;
            }
            if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                throw new ArgumentException($"Ожидается целое число для {option}");
            if (option != "--seed" && number < 1)
                throw new ArgumentException($"Минимальное значение {option}: 1");
            result = option switch
            {
                "--interval-ms" => result with { IntervalMs = number },
                "--count" => result with { Count = number },
                _ => result with { Seed = number }
            };
        }
        return result;
    }
}
