using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;

namespace LaypersonLogViewer.App.Views;

public partial class StatisticsPanel : UserControl
{
    public StatisticsPanel()
    {
        InitializeComponent();
        // Select the row before its expander consumes the pointer event.
        PatternsList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var row = (e.Source as Control)?.GetVisualAncestors().OfType<Control>()
                .Select(control => control.DataContext).OfType<StatisticsPatternRow>().FirstOrDefault();
            if (row is not null) PatternsList.SelectedItem = row;
        }, RoutingStrategies.Tunnel);
    }
    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window) await window.EditStatisticsAsync();
    }
    private async void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StatisticsViewModel { SelectedPattern: { } row }
            && TopLevel.GetTopLevel(this) is MainWindow window)
            await window.EditStatisticsAsync(row.Pattern);
    }
    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StatisticsViewModel model) model.DeleteSelected();
    }
}
