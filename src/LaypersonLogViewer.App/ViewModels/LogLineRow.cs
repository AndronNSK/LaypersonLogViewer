using LaypersonLogViewer.Core;
using Avalonia;

namespace LaypersonLogViewer.App.ViewModels;

// Display state belongs to the UI, keeping the original log line unchanged.
public sealed record LogLineRow(LogLine Line, bool IsFilteredOut, bool IsEntryStart = false)
{
    public int Number => Line.Number;
    public string Text => Line.Text;
    public double Opacity => IsFilteredOut ? 0.4 : 1.0;
    public Thickness SeparatorThickness => new(0, IsEntryStart ? 1 : 0, 0, 0);
}
