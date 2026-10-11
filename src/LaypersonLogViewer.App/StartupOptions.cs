namespace LaypersonLogViewer.App;

public sealed record StartupOptions(bool StandardInput, string? FilePath)
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return new(false, null);
        if (args.Count == 1 && args[0] == "--stdin") return new(true, null);
        if (args.Count == 2 && args[0] == "--open" && !string.IsNullOrWhiteSpace(args[1]))
            return new(false, args[1]);
        if (args.Count == 1 && !string.IsNullOrWhiteSpace(args[0]) && !args[0].StartsWith("--", StringComparison.Ordinal))
            return new(false, args[0]);
        throw new ArgumentException("Используйте --stdin или --open \"путь к файлу\". Можно передать один путь без --open.");
    }
}
