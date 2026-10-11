using System.Text;
using Avalonia.Headless.XUnit;
using LaypersonLogViewer.App;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;

namespace LaypersonLogViewer.Tests;

public sealed class StartupTests
{
    [Fact]
    public void NoArgumentsAndStandardInputRemainSupported()
    {
        Assert.Equal(new StartupOptions(false, null), StartupOptions.Parse([]));
        Assert.Equal(new StartupOptions(true, null), StartupOptions.Parse(["--stdin"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilePathsWithSpacesAndUnicodeRemainIntact(bool explicitOption)
    {
        const string path = "папка с пробелами/журнал & данные.log";
        Assert.Equal(new StartupOptions(false, path), StartupOptions.Parse(explicitOption ? ["--open", path] : [path]));
    }

    [Theory]
    [InlineData("--open")]
    [InlineData("--unknown")]
    [InlineData("")]
    [InlineData("--open", " ")]
    [InlineData("--stdin", "example.log")]
    [InlineData("first.log", "second.log")]
    [InlineData("--open", "example.log", "--stdin")]
    public void InvalidOrConflictingArgumentsAreRejected(params string[] args) =>
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(args));

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupOpensAndFollowsAFileBeingWrittenWithoutPicker(bool explicitOption)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + " журнал & данные.log");
        var window = new MainWindow();
        window.Show();
        try
        {
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("INFO первая строка\n"), TestContext.Current.CancellationToken);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            await window.StartFromArgumentsAsync(explicitOption ? ["--open", path] : [path]);
            var model = (MainWindowViewModel)window.DataContext!;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (model.AllLines.Count != 1) await Task.Delay(10, timeout.Token);
            Assert.Equal(path, model.FileName);
            Assert.Equal("INFO первая строка", model.AllLines[0].Text);
            Assert.True(model.Live.IsActive);
            Assert.Empty(window.OwnedWindows);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("ERROR вторая строка\n"), TestContext.Current.CancellationToken);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            while (model.AllLines.Count != 2) await Task.Delay(10, timeout.Token);
            Assert.Equal("ERROR вторая строка", model.AllLines[1].Text);
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFileOrInvalidArgumentsReportErrorAndPreserveCurrentLog(bool invalidArguments)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("existing line"));
            await model.LoadAsync("old.log", stream);
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
            await window.StartFromArgumentsAsync(invalidArguments ? ["--open"] : ["--open", missing]);
            Assert.True(model.HasError);
            Assert.Equal("old.log", model.FileName);
            Assert.Equal("existing line", Assert.Single(model.AllLines).Text);
            Assert.False(model.Live.HasSession);
        }
        finally { window.Close(); }
    }
}
