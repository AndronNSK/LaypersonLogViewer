using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class StatisticsPatternDialog : Window
{
    public StatisticsEditorViewModel Model { get; }
    public StatisticsPatternDialog() : this("", []) { }
    public StatisticsPatternDialog(string example, IReadOnlyList<LogLine> lines, StatisticsPattern? pattern = null)
    {
        InitializeComponent();
        Model = new StatisticsEditorViewModel(example, lines, pattern);
        DataContext = Model;
        ManualRegexBox.IsChecked = Model.ManualRegex;
        Model.PropertyChanged += (_, _) => RenderMarks();
        Closed += (_, _) => Model.Dispose();
        RenderMarks();
    }

    private void RenderMarks()
    {
        var inlines = new InlineCollection();
        var position = 0;
        foreach (var row in Model.Ranges.OrderBy(row => row.Range.Start))
        {
            var range = row.Range;
            if (range.Start < position || range.Start > Model.Example.Length - range.Length) continue;
            inlines.Add(new Run(Model.Example[position..range.Start]));
            inlines.Add(new Run(Model.Example.Substring(range.Start, range.Length))
            {
                Foreground = range.Kind == StatisticsRangeKind.Anchor ? Brushes.RoyalBlue : Brushes.ForestGreen,
                FontWeight = FontWeight.Bold
            });
            position = range.Start + range.Length;
        }
        inlines.Add(new Run(Model.Example[position..]));
        MarkedExample.Inlines = inlines;
    }

    private void Anchor_Click(object? sender, RoutedEventArgs e) => Model.AddRange(
        Math.Min(ExampleBox.SelectionStart, ExampleBox.SelectionEnd),
        Math.Abs(ExampleBox.SelectionEnd - ExampleBox.SelectionStart), StatisticsRangeKind.Anchor);

    private async void Number_Click(object? sender, RoutedEventArgs e)
    {
        var start = Math.Min(ExampleBox.SelectionStart, ExampleBox.SelectionEnd);
        var length = Math.Abs(ExampleBox.SelectionEnd - ExampleBox.SelectionStart);
        var name = await new StatisticsPromptDialog("Название числового значения", "Название", true).ShowDialog<string?>(this);
        if (name is not null) Model.AddRange(start, length, StatisticsRangeKind.Number, name);
    }

    private void RemoveRange_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StatisticsRangeRow row }) Model.RemoveRange(row);
    }

    private async void ManualRegex_Click(object? sender, RoutedEventArgs e)
    {
        if (ManualRegexBox.IsChecked != true && Model.ManualRegex)
        {
            var answer = await new StatisticsPromptDialog("Вернуться к выделенным фрагментам?",
                "Ручной regex будет заменён шаблоном из выделенных фрагментов.", false).ShowDialog<string?>(this);
            if (answer is null) { ManualRegexBox.IsChecked = true; return; }
        }
        Model.SetManualRegex(ManualRegexBox.IsChecked == true);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.CanSave) Close(Model.BuildPattern());
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}

public sealed class StatisticsPromptDialog : Window
{
    public StatisticsPromptDialog(string title, string message, bool input)
    {
        Title = title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var box = new TextBox { Name = "ValueNameBox", IsVisible = input };
        var accept = new Button { Name = "AcceptButton", Content = input ? "Добавить" : "Заменить", IsDefault = true, IsEnabled = !input };
        var cancel = new Button { Name = "CancelButton", Content = "Отмена", IsCancel = true };
        box.TextChanged += (_, _) => accept.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        accept.Click += (_, _) => Close(input ? box.Text : "yes");
        cancel.Click += (_, _) => Close(null);
        Content = new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8, Children =
        {
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, box,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { cancel, accept } }
        }};
        Opened += (_, _) => { if (input) box.Focus(); };
    }
}
