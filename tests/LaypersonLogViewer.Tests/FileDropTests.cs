using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.App.Views;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.Tests;

public sealed class FileDropTests
{
    [AvaloniaFact]
    public async Task OpeningFileUsesRetentionLimitAndReplacesPreviousFollowSession()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        var secondPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            model.Live.RetainedLines = 100;
            await File.WriteAllTextAsync(firstPath, string.Concat(Enumerable.Range(1, 120).Select(i => $"line {i}\n")),
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(secondPath, "second file\n", TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var firstData = new DataTransfer();
            firstData.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFile>(
                await window.StorageProvider.TryGetFileFromPathAsync(firstPath))));
            Raise(window, DragDrop.DropEvent, firstData);
            while (model.AllLines.Count != 100 || model.IsBusy) await Task.Delay(10, timeout.Token);
            Assert.Equal(21, model.AllLines[0].Number);
            Assert.Equal("line 120", model.AllLines[^1].Text);
            var previous = model.Live.Completion;
            using var secondData = new DataTransfer();
            secondData.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFile>(
                await window.StorageProvider.TryGetFileFromPathAsync(secondPath))));
            Raise(window, DragDrop.DropEvent, secondData);
            await previous.WaitAsync(timeout.Token);
            while (model.FileName != secondPath || model.AllLines.Count != 1 || model.IsBusy)
                await Task.Delay(10, timeout.Token);
            Assert.Equal("second file", Assert.Single(model.AllLines).Text);
            await File.AppendAllTextAsync(firstPath, "old source\n", TestContext.Current.CancellationToken);
            await File.AppendAllTextAsync(secondPath, "new source\n", TestContext.Current.CancellationToken);
            while (model.AllLines.Count != 2 || model.IsBusy) await Task.Delay(10, timeout.Token);
            Assert.Equal(new[] { "second file", "new source" }, model.AllLines.Select(line => line.Text));
            Assert.True(model.Live.IsActive);
        }
        finally
        {
            window.Close();
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [AvaloniaFact]
    public async Task DropOverLogTextFollowsSharedFileAndKeepsFiltersAndStatistics()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            Assert.NotNull(window.FindControl<Button>("OpenFileButton"));
            Assert.Null(window.FindControl<Button>("FollowFileButton"));
            using var initial = new MemoryStream(Encoding.UTF8.GetBytes("ERROR old x=1"));
            await model.LoadAsync("old.log", initial);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            model.Statistics.SavePattern(StatisticsTests.Pattern());
            window.UpdateLayout();
            var target = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().Single();
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("INFO x=9\nERROR Привет x=4\n"), TestContext.Current.CancellationToken);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            var file = Assert.IsAssignableFrom<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path));
            using var data = new DataTransfer();
            data.Add(DataTransferItem.CreateFile(file));
            var over = Raise(target, DragDrop.DragOverEvent, data);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);
            var drop = Raise(target, DragDrop.DropEvent, data);
            Assert.True(drop.Handled);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (model.FileName != path || model.AllLines.Count != 2 || model.IsBusy) await Task.Delay(10, timeout.Token);
            await model.Statistics.CurrentCalculation;
            Assert.Equal(2, model.AllLines.Count);
            Assert.Equal("ERROR Привет x=4", Assert.Single(model.VisibleLines).Text);
            Assert.Single(model.Filters);
            Assert.Equal("4", Assert.Single(model.Statistics.Patterns).Values[0].Mean);
            Assert.True(model.Live.IsActive);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("INFO x=10\nERROR appended x=8\n"), TestContext.Current.CancellationToken);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            while (model.AllLines.Count != 4 || model.IsBusy) await Task.Delay(10, timeout.Token);
            await model.Statistics.CurrentCalculation;
            Assert.Equal(2, model.VisibleLines.Count);
            Assert.Equal("6", model.Statistics.Patterns[0].Values[0].Mean);
            model.Live.Stop();
            await model.Live.Completion;
            await writer.WriteAsync(Encoding.UTF8.GetBytes("ERROR after stop x=100\n"), TestContext.Current.CancellationToken);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
            await model.Live.RefreshAsync(model);
            Assert.Equal(4, model.AllLines.Count);
        }
        finally { window.Close(); File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task RejectsTextFoldersMultipleFilesAndDropsWhileDialogIsOpen()
    {
        var window = new MainWindow();
        window.Show();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        try
        {
            await File.WriteAllTextAsync(path, "log", TestContext.Current.CancellationToken);
            using var text = new DataTransfer();
            text.Add(DataTransferItem.CreateText(path));
            Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DragOverEvent, text).DragEffects);
            using var folder = new DataTransfer();
            folder.Add(DataTransferItem.CreateFile(Assert.IsAssignableFrom<IStorageFolder>(
                await window.StorageProvider.TryGetFolderFromPathAsync(Path.GetTempPath()))));
            Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DragOverEvent, folder).DragEffects);
            using var file = Assert.IsAssignableFrom<IStorageFile>(await window.StorageProvider.TryGetFileFromPathAsync(path));
            using var multiple = new DataTransfer();
            multiple.Add(DataTransferItem.CreateFile(file));
            multiple.Add(DataTransferItem.CreateFile(file));
            Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DragOverEvent, multiple).DragEffects);
            Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DropEvent, multiple).DragEffects);
            using var single = new DataTransfer();
            single.Add(DataTransferItem.CreateFile(file));
            var dialog = new Window();
            dialog.Show(window);
            try
            {
                Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DragOverEvent, single).DragEffects);
                Assert.Equal(DragDropEffects.None, Raise(window, DragDrop.DropEvent, single).DragEffects);
            }
            finally { dialog.Close(); }
            Assert.Empty(((MainWindowViewModel)window.DataContext!).AllLines);
        }
        finally { window.Close(); File.Delete(path); }
    }

    private static DragEventArgs Raise(Control target, Avalonia.Interactivity.RoutedEvent<DragEventArgs> routedEvent,
        IDataTransfer data)
    {
        var args = new DragEventArgs(routedEvent, data, target, new Point(1, 1), KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy | DragDropEffects.Move
        };
        target.RaiseEvent(args);
        return args;
    }
}
