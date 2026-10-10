using System.Text;

namespace LaypersonLogViewer.FakeLogs;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var options = GeneratorOptions.Parse(args);
            if (options.Help)
            {
                await Console.Error.WriteLineAsync("""
                    LaypersonLogViewer.FakeLogs — генератор тестовых журналов
                    --interval-ms N  Интервал между записями, минимум 1, по умолчанию 500
                    --count N        Число записей; без параметра работает до Ctrl+C
                    --seed N         Начальное значение генератора чисел, по умолчанию 42
                    --file PATH      Создать файл или дописать в существующий (UTF-8)
                    --help           Эта справка

                    FakeLogs --count 20 --interval-ms 100
                    FakeLogs --file example.log --seed 42
                    LaypersonLogViewer.FakeLogs.exe 2>&1 | LaypersonLogViewer.App.exe --stdin
                    ERROR направляется в stderr, остальные записи — в stdout.
                    В режиме --file все записи направляются только в файл.
                    """);
                return 0;
            }
            await LogGenerator.RunAsync(options, Console.Out, Console.Error, stop.Token);
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await Console.Error.WriteLineAsync($"Ошибка: {e.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
