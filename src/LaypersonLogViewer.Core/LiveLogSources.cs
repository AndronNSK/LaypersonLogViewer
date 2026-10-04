using System.Diagnostics;
using System.Text;

namespace LaypersonLogViewer.Core;

public static class LiveLogSources
{
    public static async Task ReadAsync(Stream stream, Action<string> receive, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        await ReadLinesAsync(reader, receive, token);
    }

    private static async Task ReadLinesAsync(TextReader reader, Action<string> receive, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            token.ThrowIfCancellationRequested();
            receive(line);
        }
    }

    public static async Task<int> RunCommandAsync(string executable, string arguments, string? directory,
        Action<string> receive, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable, arguments)
            {
                WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        token.ThrowIfCancellationRequested();
        process.Start();
        process.StandardInput.Close();
        try
        {
            await Task.WhenAll(ReadLinesAsync(process.StandardOutput, receive, token),
                ReadLinesAsync(process.StandardError, receive, token), process.WaitForExitAsync(token));
            return process.ExitCode;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    // Follow UTF-8 files. A decoder and partial line survive polls and split writes.
    public static async Task FollowFileAsync(string path, Action<string> receive, CancellationToken token,
        TimeSpan? pollInterval = null)
    {
        path = Path.GetFullPath(path);
        var generation = 0;
        using var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!)
        {
            NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true
        };
        bool IsTarget(string name) => string.Equals(name, path,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        watcher.Created += (_, e) => { if (IsTarget(e.FullPath)) Interlocked.Increment(ref generation); };
        watcher.Deleted += (_, e) => { if (IsTarget(e.FullPath)) Interlocked.Increment(ref generation); };
        watcher.Renamed += (_, e) => { if (IsTarget(e.FullPath) || IsTarget(e.OldFullPath)) Interlocked.Increment(ref generation); };
        watcher.Error += (_, _) => Interlocked.Increment(ref generation);
        var seenGeneration = Volatile.Read(ref generation);
        long position = 0;
        byte[] checkpoint = [];
        var decoder = Encoding.UTF8.GetDecoder();
        var lines = new LineAccumulator(receive);
        var bytes = new byte[4096];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileStream? file = null;
                try { file = LogFileReader.OpenRead(path); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (file is not null)
                {
                    await using (file)
                    {
                        var currentGeneration = Volatile.Read(ref generation);
                        var reset = currentGeneration != seenGeneration || file.Length < position;
                        if (!reset && checkpoint.Length > 0)
                        {
                            file.Position = position - checkpoint.Length;
                            var actual = new byte[checkpoint.Length];
                            var read = await file.ReadAsync(actual, token);
                            reset = read != actual.Length || !actual.SequenceEqual(checkpoint);
                        }
                        if (reset)
                        {
                            lines.Finish();
                            lines = new LineAccumulator(receive);
                            decoder.Reset();
                            position = 0;
                            checkpoint = [];
                        }
                        seenGeneration = currentGeneration;
                        file.Position = position;
                        var end = file.Length;
                        while (position < end)
                        {
                            var read = await file.ReadAsync(bytes.AsMemory(0, (int)Math.Min(bytes.Length, end - position)), token);
                            if (read == 0) break;
                            position += read;
                            var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
                            lines.Append(chars.AsSpan(0, count));
                        }
                        checkpoint = new byte[(int)Math.Min(64, position)];
                        file.Position = position - checkpoint.Length;
                        var checkpointRead = await file.ReadAsync(checkpoint, token);
                        if (checkpointRead != checkpoint.Length) Interlocked.Increment(ref generation);
                    }
                }
                await Task.Delay(pollInterval ?? TimeSpan.FromMilliseconds(200), token);
            }
        }
        finally { lines.Finish(); }
    }

    private sealed class LineAccumulator(Action<string> receive)
    {
        private readonly StringBuilder _partial = new();
        private bool _afterCr, _first = true;
        public void Append(ReadOnlySpan<char> text)
        {
            foreach (var character in text)
            {
                if (_first) { _first = false; if (character == '\uFEFF') continue; }
                if (_afterCr && character == '\n') { _afterCr = false; continue; }
                _afterCr = character == '\r';
                if (character is '\r' or '\n') { receive(_partial.ToString()); _partial.Clear(); }
                else _partial.Append(character);
            }
        }
        public void Finish()
        {
            if (_partial.Length > 0) { receive(_partial.ToString()); _partial.Clear(); }
        }
    }
}
