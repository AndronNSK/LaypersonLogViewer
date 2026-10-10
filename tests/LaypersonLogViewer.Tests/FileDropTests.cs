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
    public async Task DropOverLogTextOpensSharedFileAndKeepsFiltersAndStatistics()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log");
        var window = new MainWindow();
        window.Show();
        try
        {
            var model = (MainWindowViewModel)window.DataContext!;
            using var initial = new MemoryStream(Encoding.UTF8.GetBytes("ERROR old x=1"));
            await model.LoadAsync("old.log", initial);
            await model.AddFilterAsync(new LogFilter(FilterKind.Include, "ERROR"));
            model.Statistics.SavePattern(StatisticsTests.Pattern());
            window.UpdateLayout();
            var target = window.FindControl<ListBox>("LogLines")!.GetVisualDescendants().OfType<TextBox>().Single();
            using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            await writer.WriteAsync(Encoding.UTF8.GetBytes("INFO x=9\nERROR Привет x=4"), TestContext.Current.CancellationToken);
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
            while (model.FileName != Path.GetFileName(path) || model.IsBusy) await Task.Delay(10, timeout.Token);
            await model.Statistics.CurrentCalculation;
            Assert.Equal(2, model.AllLines.Count);
            Assert.Equal("ERROR Привет x=4", Assert.Single(model.VisibleLines).Text);
            Assert.Single(model.Filters);
            Assert.Equal("4", Assert.Single(model.Statistics.Patterns).Values[0].Mean);
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
