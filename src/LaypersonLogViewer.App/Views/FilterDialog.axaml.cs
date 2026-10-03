using Avalonia.Controls;
using Avalonia.Interactivity;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class FilterDialog : Window
{
    private readonly FilterKind _kind;

    public FilterDialog() : this(FilterKind.Include) { }

    public FilterDialog(FilterKind kind, string initialText = "", bool addingCondition = false)
    {
        _kind = kind;
        InitializeComponent();
        Title = addingCondition ? "Добавить условие — И содержит"
            : kind == FilterKind.Include ? "Показать строки" : "Скрыть строки";
        Heading.Text = Title;
        PatternBox.Text = initialText;
        AddButton.IsEnabled = !string.IsNullOrWhiteSpace(initialText);
        Opened += (_, _) =>
        {
            PatternBox.Focus();
            PatternBox.SelectAll();
        };
    }

    private void Pattern_Changed(object? sender, TextChangedEventArgs e)
    {
        if (AddButton is not null)
            AddButton.IsEnabled = !string.IsNullOrWhiteSpace(PatternBox.Text);
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(PatternBox.Text))
            Close(new LogFilter(_kind, PatternBox.Text, CaseSensitiveBox.IsChecked == true));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
