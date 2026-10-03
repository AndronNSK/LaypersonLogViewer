using Avalonia.Controls;
using Avalonia.Interactivity;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class TimestampPatternDialog : Window
{
    private readonly TimestampPattern _pattern;

    public TimestampPatternDialog() : this(new TimestampPattern("2026-10-03 12:34:56", 0), 0) { }

    public TimestampPatternDialog(TimestampPattern pattern, int matchingStarts)
    {
        _pattern = pattern;
        InitializeComponent();
        ExampleBox.Text = pattern.Example;
        PatternDescription.Text = $"Шаблон: {pattern.Shape}\nПозиция первого символа: {pattern.StartIndex + 1}";
        PreviewCount.Text = $"Найдено начал записей во всём файле: {matchingStarts:N0}";
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) => Close(_pattern);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
