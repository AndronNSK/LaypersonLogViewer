using System.Text.Json;

namespace LaypersonLogViewer.Core;

public sealed record CommandRequest(string Executable, string Arguments, string Directory);

/// <summary>Recent confirmed commands, with an optional JSON file for persistence.</summary>
public sealed class CommandHistory(string? path = null)
{
    public const int MaximumEntries = 20;
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaypersonLogViewer", "command-history.json");
    private sealed record Document(int FormatVersion, CommandRequest[] Commands);
    private CommandRequest[] _entries = [];
    private bool _loaded;
    public IReadOnlyList<CommandRequest> Entries => Array.AsReadOnly(_entries);

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        if (path is null || !File.Exists(path)) return;
        try
        {
            using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<Document>(stream);
            if (document is null || document.FormatVersion != 1 || document.Commands is null
                || document.Commands.Any(command => command is null || string.IsNullOrWhiteSpace(command.Executable)
                    || command.Arguments is null || command.Directory is null))
                throw new InvalidDataException("Неподдерживаемый формат истории команд.");
            _entries = document.Commands.Select(Normalize).Distinct().Take(MaximumEntries).ToArray();
        }
        catch (JsonException e) { throw new InvalidDataException("Повреждён файл истории команд.", e); }
    }

    public async Task RememberAsync(CommandRequest command)
    {
        command = Normalize(command);
        _entries = new[] { command }.Concat(_entries.Where(entry => entry != command)).Take(MaximumEntries).ToArray();
        if (path is null) return;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var contents = JsonSerializer.SerializeToUtf8Bytes(new Document(1, _entries),
            new JsonSerializerOptions { WriteIndented = true });
        await PatternFiles.WriteFileAsync(path, contents);
    }

    private static CommandRequest Normalize(CommandRequest command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Executable);
        ArgumentNullException.ThrowIfNull(command.Arguments);
        ArgumentNullException.ThrowIfNull(command.Directory);
        return command with { Executable = command.Executable.Trim() };
    }
}
