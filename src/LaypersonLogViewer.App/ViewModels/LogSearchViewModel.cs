using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class LogSearchViewModel : ObservableModel, IDisposable
{
    private IReadOnlyList<LogLine> _lines = [];
    private CancellationTokenSource? _pending;
    private string _text = "";
    private bool _caseSensitive, _visible;
    private LogSearchMatch? _current;
    private LogLine? _startLine;
    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; Reset(); }
    }
    public bool CaseSensitive
    {
        get => _caseSensitive;
        set { if (_caseSensitive == value) return; _caseSensitive = value; Reset(); }
    }
    public bool IsVisible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            if (!value) Cancel();
            Notify(nameof(IsVisible));
            Notify(nameof(HighlightText));
            NotifyState();
        }
    }
    public string HighlightText => IsVisible ? Text : "";
    public LogSearchMatch? CurrentMatch => _current;
    public bool IsSearching => _pending is not null;
    public bool CanSearch => Text.Length > 0 && !IsSearching;
    public string Status { get; private set; } = "";

    public void SetLines(IReadOnlyList<LogLine> lines)
    {
        if (ReferenceEquals(_lines, lines)) return;
        _lines = lines;
        Cancel();
        if (_startLine is not null && LogSearch.IndexOfLine(lines, _startLine) < 0) _startLine = null;
        if (_current is not null && LogSearch.IndexOfLine(lines, _current.Line) < 0)
        { _current = null; Status = ""; Notify(nameof(CurrentMatch)); }
        NotifyState();
    }

    public void StartAt(LogLine line)
    {
        _startLine = line;
        Reset();
    }

    public async Task<LogSearchMatch?> FindAsync(bool backwards = false)
    {
        Cancel();
        if (Text.Length == 0) return null;
        using var pending = new CancellationTokenSource();
        _pending = pending;
        var lines = _lines;
        var text = Text;
        var sensitive = CaseSensitive;
        var current = _current;
        var startLine = _startLine;
        Status = "Поиск…";
        NotifyState();
        try
        {
            var match = await Task.Run(() =>
                LogSearch.Find(lines, text, sensitive, current, backwards, pending.Token, startLine), pending.Token);
            if (pending.IsCancellationRequested || !ReferenceEquals(_pending, pending)) return null;
            _current = match;
            if (match is not null) _startLine = null;
            Status = match is null ? "Не найдено"
                : $"Строка {match.Line.Number:N0}" + (match.Wrapped ? " · Поиск по кругу" : "");
            Notify(nameof(CurrentMatch));
            return match;
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { return null; }
        finally
        {
            if (ReferenceEquals(_pending, pending)) { _pending = null; NotifyState(); }
        }
    }

    private void Reset() { Cancel(); _current = null; Status = ""; Notify(); }
    private void NotifyState()
    {
        Notify(nameof(IsSearching));
        Notify(nameof(CanSearch));
        Notify(nameof(Status));
    }
    private void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
        if (Status == "Поиск…") Status = "";
    }
    public void Dispose() => Cancel();
}
