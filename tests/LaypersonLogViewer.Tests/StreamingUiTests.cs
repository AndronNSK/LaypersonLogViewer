using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;

namespace LaypersonLogViewer.Tests;

public sealed class StreamingUiTests
{
    [AvaloniaFact]
    public void CommandDialogCancelDoesNotStartCapture()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            window.FindControl<Button>("StartCommandButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var dialog = Assert.IsType<CommandDialog>(Assert.Single(window.OwnedWindows));
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.False(buttons.Single(b => b.Name == "RunCommandButton").IsEnabled);
            dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ExecutableBox").Text = "dotnet";
            Dispatcher.UIThread.RunJobs();
            Assert.True(buttons.Single(b => b.Name == "RunCommandButton").IsEnabled);
            buttons.Single(b => b.Name == "CancelCommandButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(((MainWindowViewModel)window.DataContext!).Live.HasSession);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task FollowLatestPreservesHorizontalPositionAndPauseFreezesDisplay()
    {
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            var ready = new TaskCompletionSource<Action<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            model.Live.Start("UI stream", async (receive, token) =>
            {
                for (var i = 0; i < 200; i++) receive(new string('x', 300));
                ready.SetResult(receive);
                await Task.Delay(Timeout.Infinite, token);
                return "done";
            });
            var write = await ready.Task;
            await WaitFor(() => model.AllLines.Count == 200 && !model.IsBusy);
            window.UpdateLayout();
            window.MouseMove(new Point(0, 0));
            var list = window.FindControl<ListBox>("LogLines")!;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single(s => ReferenceEquals(s.TemplatedParent, list));
            model.Live.FollowLatest = false;
            scroll.Offset = new Vector(80, 0);
            write("next");
            await WaitFor(() => model.AllLines.Count == 201 && !model.IsBusy);
            Assert.Equal(80, scroll.Offset.X);
            Assert.Equal(0, scroll.Offset.Y);
            model.Live.FollowLatest = true;
            write("last");
            await WaitFor(() => model.AllLines.Count == 202 && !model.IsBusy && scroll.Offset.Y > 0);
            Assert.Equal(80, scroll.Offset.X);
            window.UpdateLayout();
            window.MouseMove(new Point(0, 0));
            var listOrigin = list.TranslatePoint(default, window)!.Value;
            var text = list.GetVisualDescendants().OfType<TextBox>().First(t =>
            {
                var y = t.TranslatePoint(default, window)!.Value.Y;
                return y >= listOrigin.Y && y + 20 < listOrigin.Y + list.Bounds.Height;
            });
            var click = new Point(listOrigin.X + 130, text.TranslatePoint(default, window)!.Value.Y + 5);
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Assert.False(model.Live.FollowLatest);
            Assert.False(window.FindControl<CheckBox>("FollowLatestBox")!.IsChecked);
            var stoppedOffset = scroll.Offset;
            write("arrived while reading");
            await WaitFor(() => model.AllLines.Count == 203 && !model.IsBusy);
            Assert.False(model.Live.FollowLatest);
            Assert.Equal(stoppedOffset, scroll.Offset);
            scroll.Offset = new Vector(80, scroll.ScrollBarMaximum.Y - 100);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.Live.FollowLatest);
            scroll.Offset = new Vector(80, scroll.ScrollBarMaximum.Y);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.Live.FollowLatest);
            window.FindControl<CheckBox>("PauseStreamBox")!.IsChecked = true;
            write("paused arrival");
            await Task.Delay(650, TestContext.Current.CancellationToken);
            Assert.Equal(203, model.AllLines.Count);
            Assert.Equal(204, model.Live.Snapshot().Length);
            window.FindControl<CheckBox>("PauseStreamBox")!.IsChecked = false;
            await WaitFor(() => model.AllLines.Count == 204 && !model.IsBusy);
            model.Live.Stop();
            await model.Live.Completion;
        }
        finally { window.Close(); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
