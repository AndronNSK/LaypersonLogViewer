using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class JsonFormattingTests
{
    [Fact]
    public void FormatsNestedJsonWithTwoSpacesPreservingTokensAndOffsets()
    {
        const string raw = """INFO {"name":"Привет","data":[1,{"escaped":"a\"b","amount":1e+12}],"empty":{}}""";
        var result = JsonLogFormatter.Format(raw)!;
        Assert.NotNull(result);
        Assert.StartsWith("INFO\n{\n", result.Text);
        Assert.Contains("\n  \"name\": \"Привет\",\n", result.Text);
        Assert.Contains("\n    1,", result.Text);
        Assert.Contains("\"empty\": {}", result.Text);
        Assert.Contains("1e+12", result.Text);
        var start = raw.IndexOf("Привет", StringComparison.Ordinal);
        Assert.Equal("Привет", result.Text[result.Offsets[start]..result.Offsets[start + 6]]);
        Assert.Equal(raw.Length + 1, result.Offsets.Length);
    }

    [Theory]
    [InlineData("{\"x\":1}", "{\n  \"x\": 1\n}")]
    [InlineData("INFO   {\"x\":1}", "INFO\n{\n  \"x\": 1\n}")]
    [InlineData("[2026-11-21] INFO [1,2]", "[2026-11-21] INFO\n[\n  1,\n  2\n]")]
    public void JsonStartsOnItsOwnLineAfterAPrefix(string raw, string expected)
    {
        var result = JsonLogFormatter.Format(raw)!;
        Assert.Equal(expected, result.Text);
        var start = raw.LastIndexOfAny(['{', '[']);
        Assert.Equal(raw[start], result.Text[result.Offsets[start]]);
    }

    [Theory]
    [InlineData("ordinary text")]
    [InlineData("{broken}")]
    [InlineData("{\"x\":1} trailing")]
    [InlineData("{\n\"x\":1}")]
    public void UnformattableLinesAreKeptAsSource(string text) => Assert.Null(JsonLogFormatter.Format(text));

    [AvaloniaFact]
    public async Task DisplayFormattingPreservesRawFilteringAndFindsSearchTextAtFormattedOffset()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            const string raw = """{"level":"ERROR","nested":{"message":"Привет"}}""";
            var model = (MainWindowViewModel)window.DataContext!;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
            await model.LoadAsync("json.log", stream);
            model.FormatJson = true;
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Contains("\n  \"nested\": {\n", text.Text);
            Assert.Equal(raw, model.AllLines[0].Text);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "\"level\":\"ERROR\""));
            Assert.Single(model.VisibleLines);
            model.Search.IsVisible = true;
            model.Search.Text = "Привет";
            window.FindControl<Button>("SearchNextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (text.SelectedText != "Привет") await Task.Delay(10, timeout.Token);
            Assert.True(text.SelectionStart > raw.IndexOf("Привет", StringComparison.Ordinal));
            model.FormatJson = false;
            window.UpdateLayout();
            Assert.Equal(raw, text.Text);
        }
        finally { window.Close(); }
    }
}
