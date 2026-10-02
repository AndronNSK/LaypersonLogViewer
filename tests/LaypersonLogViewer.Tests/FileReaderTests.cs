using System.Text;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FileReaderTests
{
    [Fact]
    public async Task ReadsFileWhileWriterIsOpenAndAllowsFurtherWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-viewer-{Guid.NewGuid():N}.log");
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            // A typical logger permits readers but keeps its write handle open.
            await using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("INFO started\n"), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            await using var reader = LogFileReader.OpenRead(path);
            var lines = await LogFileReader.ReadAsync(reader, cancellationToken);
            Assert.Equal("INFO started", Assert.Single(lines).Text);
            Assert.False(reader.CanWrite);

            // Keeping the viewer open must not stop the logger appending more data.
            await writer.WriteAsync(Encoding.UTF8.GetBytes("ERROR later\n"), cancellationToken);
            await writer.FlushAsync(cancellationToken);
            await using var reopenedReader = LogFileReader.OpenRead(path);
            var updated = await LogFileReader.ReadAsync(reopenedReader, cancellationToken);
            Assert.Equal(new[] { "INFO started", "ERROR later" }, updated.Select(line => line.Text));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AllowsLogRotationWhileReaderIsOpen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-viewer-{Guid.NewGuid():N}.log");
        var rotatedPath = path + ".old";
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "Old log\n", cancellationToken);
            await using var reader = LogFileReader.OpenRead(path);
            File.Move(path, rotatedPath);
            await File.WriteAllTextAsync(path, "New log\n", cancellationToken);

            Assert.Equal("Old log", Assert.Single(await LogFileReader.ReadAsync(reader, cancellationToken)).Text);
            await using var newReader = LogFileReader.OpenRead(path);
            Assert.Equal("New log", Assert.Single(await LogFileReader.ReadAsync(newReader, cancellationToken)).Text);
        }
        finally
        {
            File.Delete(path);
            File.Delete(rotatedPath);
        }
    }

    [Fact]
    public async Task ReadsMixedNewlinesBlankLinesAndCyrillic()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Первая\r\n\r\nThird\nLast\r"));
        var lines = await LogFileReader.ReadAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "Первая", "", "Third", "Last" }, lines.Select(l => l.Text));
        Assert.Equal(new[] { 1, 2, 3, 4 }, lines.Select(l => l.Number));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task EmptyFileHasNoLines()
    {
        using var stream = new MemoryStream();
        Assert.Empty(await LogFileReader.ReadAsync(stream, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-32")]
    public async Task DetectsByteOrderMarks(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes("Ошибка")).ToArray());
        Assert.Equal("Ошибка", Assert.Single(await LogFileReader.ReadAsync(stream, TestContext.Current.CancellationToken)).Text);
    }

    [Fact]
    public async Task InvalidUtf8IsReportedInsteadOfSilentlyReplacingText()
    {
        using var stream = new MemoryStream(new byte[] { 0xff, 0xff, 0xff });
        await Assert.ThrowsAsync<DecoderFallbackException>(() => LogFileReader.ReadAsync(stream, TestContext.Current.CancellationToken));
    }
}
