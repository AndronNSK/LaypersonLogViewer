using System.Globalization;
using Avalonia.Data.Converters;
using LaypersonLogViewer.App.ViewModels;

namespace LaypersonLogViewer.App.Views;

public sealed class LogDisplayConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is LogLineRow row ? row.DisplayText(values[1] is true) : "";
}
