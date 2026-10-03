using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class TimestampSelectionTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectedTimestampCanBePreviewedAcceptedOrCancelled(bool accept)
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            const string stamp = "2026-10-03 12:34:56";
            var model = (MainWindowViewModel)window.DataContext!;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                $"[{stamp}] ERROR failed\n stack trace\n[2026-10-03 12:34:57] INFO ready"));
            await model.LoadAsync("test.log", stream);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var text = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<SelectableTextBlock>().First();
            text.Focus();
            // Reverse selection must produce the same start position.
            text.SelectionStart = 1 + stamp.Length;
            text.SelectionEnd = 1;
            window.MouseMove(new Point(0, 0));
            var point = text.TranslatePoint(new Point(5, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            var menu = text.ContextMenu!;
            var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Начало записи по времени..."));
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (window.OwnedWindows.Count == 0)
                await Task.Delay(10, timeout.Token);
            var dialog = Assert.IsType<TimestampPatternDialog>(Assert.Single(window.OwnedWindows));
            Assert.Equal(stamp, dialog.FindControl<TextBox>("ExampleBox")!.Text);
            Assert.Contains("2", dialog.FindControl<TextBlock>("PreviewCount")!.Text);
            Assert.False(model.HasTimestampPattern);
            dialog.FindControl<Button>(accept ? "ApplyButton" : "CancelButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (accept)
            {
                while (!model.HasTimestampPattern || model.IsBusy)
                    await Task.Delay(10, timeout.Token);
                Assert.Equal(1, model.TimestampPattern!.StartIndex);
                Assert.Equal(stamp, model.TimestampPattern.Example);
                Assert.Equal(2, model.EntryCount);
                Assert.Equal(new[] { 1, 2 }, model.DisplayLines.Select(row => row.Number));
                window.FindControl<Button>("ResetTimestampButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                while (model.HasTimestampPattern || model.IsBusy)
                    await Task.Delay(10, timeout.Token);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.HasTimestampPattern);
            Assert.Single(model.DisplayLines);
            Assert.Single(model.Filters);
        }
        finally { window.Close(); }
    }
}
