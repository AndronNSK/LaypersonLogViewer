using System.Text;

namespace LaypersonLogViewer.Core;

public static class LogFileReader
{
    public static FileStream OpenRead(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        // Let the logger keep writing and allow rename/delete during log rotation.
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
    });

    // UTF-8 by default; StreamReader also detects UTF-8/UTF-16/UTF-32 byte-order marks.
    // Leave the stream open: its caller owns and disposes it.
    public static async Task<IReadOnlyList<LogLine>> ReadAsync(
        Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
            detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var lines = new List<LogLine>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } text)
            lines.Add(new LogLine(lines.Count + 1, text));
        return lines;
    }
}
