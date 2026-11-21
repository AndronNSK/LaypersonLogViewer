using LaypersonLogViewer.Core;
using Avalonia;

namespace LaypersonLogViewer.App.ViewModels;

// Display state belongs to the UI, keeping the original log line unchanged.
public sealed class LogLineRow(LogLine line, bool isFilteredOut, bool isEntryStart = false) : ObservableModel
{
    private bool _formatAttempted;
    private FormattedLogText? _formatted;
    public string DisplayText(bool format) => format ? Formatted?.Text ?? Text : Text;
    public int DisplayOffset(int offset, bool format) => format && Formatted is { } value
        ? value.Offsets[Math.Clamp(offset, 0, Text.Length)] : offset;
    private FormattedLogText? Formatted
    {
        get
        {
            if (!_formatAttempted) { _formatted = JsonLogFormatter.Format(Text); _formatAttempted = true; }
            return _formatted;
        }
    }
    public LogLine Line { get; } = line;
    public bool IsFilteredOut { get; private set; } = isFilteredOut;
    public bool IsEntryStart { get; private set; } = isEntryStart;
    public int Number => Line.Number;
    public string Text => Line.Text;
    public double Opacity => IsFilteredOut ? 0.4 : 1.0;
    public Thickness SeparatorThickness => new(0, IsEntryStart ? 1 : 0, 0, 0);
    public void UpdateDisplayState(bool filteredOut, bool entryStart)
    {
        if (IsFilteredOut == filteredOut && IsEntryStart == entryStart) return;
        IsFilteredOut = filteredOut;
        IsEntryStart = entryStart;
        Notify();
    }
}
