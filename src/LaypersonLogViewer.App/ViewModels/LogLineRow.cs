using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

// Display state belongs to the UI, keeping the original log line unchanged.
public sealed record LogLineRow(LogLine Line, bool IsFilteredOut)
{
    public int Number => Line.Number;
    public string Text => Line.Text;
    public double Opacity => IsFilteredOut ? 0.4 : 1.0;
}
