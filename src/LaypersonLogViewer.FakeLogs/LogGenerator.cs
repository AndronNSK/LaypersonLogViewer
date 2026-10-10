using System.Globalization;
using System.Text;

namespace LaypersonLogViewer.FakeLogs;

public sealed record FakeEntry(bool IsError, string Text);

public sealed class LogGenerator(int seed)
{
    private readonly Random _random = new(seed);
    private static readonly string[] Levels = ["INFO", "DEBUG", "WARN", "ERROR"];
    private static readonly string[] Categories = ["database", "network", "healthcheck"];

    public FakeEntry Next(long number, DateTimeOffset timestamp)
    {
        var level = Levels[(int)((number - 1) % Levels.Length)];
        var category = Categories[(int)((number - 1) % Categories.Length)];
        var duration = _random.Next(1, 10000) / 10.0;
        var size = _random.Next(128, 65537);
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} {level} request={number} category={category} duration={duration:0.0} size={size} ");
        text += level switch
        {
            "INFO" => "Запрос выполнен",
            "DEBUG" => "Проверка подключения",
            "WARN" => "Медленный ответ",
            _ => "timeout: соединение закрыто"
        };
        if (number % 10 == 0) text += " details=" + new string('x', 400);
        if (level == "ERROR") text += "\n    Connection failed: повторная попытка\n    at FakeClient.Send()";
        return new(level == "ERROR", text);
    }

    public static async Task RunAsync(GeneratorOptions options, TextWriter output, TextWriter error,
        CancellationToken token)
    {
        if (options.IntervalMs < 1 || options.Count is < 1) throw new ArgumentOutOfRangeException(nameof(options));
        token.ThrowIfCancellationRequested();
        StreamWriter? file = null;
        try
        {
            if (options.File is not null)
                file = new StreamWriter(new FileStream(options.File, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous), new UTF8Encoding(false));
            var generator = new LogGenerator(options.Seed);
            for (long number = 1; options.Count is null || number <= options.Count.Value; number++)
            {
                token.ThrowIfCancellationRequested();
                var entry = generator.Next(number, DateTimeOffset.Now);
                var writer = file ?? (entry.IsError ? error : output);
                // Finish and flush the whole entry before honouring cancellation, avoiding partial entries.
                await writer.WriteLineAsync(entry.Text);
                await writer.FlushAsync();
                if (options.Count != number) await Task.Delay(options.IntervalMs, token);
            }
        }
        finally { if (file is not null) await file.DisposeAsync(); }
    }
}
