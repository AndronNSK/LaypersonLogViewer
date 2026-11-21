using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

public partial class MainWindow
{
    private readonly record struct LogTextPoint(LogLineRow Row, int Offset);

    private readonly DispatcherTimer _logDragScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    // Row identities survive streaming and virtualization. Text boxes only render the visible selection.
    private (LogTextPoint Start, LogTextPoint End)? _logTextRange;
    private IPointer? _logDragPointer;
    private LogTextPoint _logDragAnchor;
    private Point _logDragPosition;
    private ScrollViewer? _logDragScroll;
    private ScrollContentPresenter? _logDragViewport;

    private string? SingleLineSelection => _logTextRange is null ? ActiveLogText?.SelectedText : null;
    private IReadOnlyList<LogLine> DisplayedLogLines =>
        ViewModel.ShowFilteredOut ? ViewModel.AllLines : ViewModel.VisibleLines;

    private void InitializeLogTextSelection()
    {
        LogLines.AddHandler(PointerPressedEvent, LogSelection_PointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, LogSelection_PointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, LogSelection_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        LogLines.PointerCaptureLost += (_, e) =>
        {
            if (ReferenceEquals(e.Source, LogLines) && ReferenceEquals(e.Pointer, _logDragPointer))
                FinishLogDrag();
        };
        LogLines.LayoutUpdated += (_, _) => RefreshLogTextRange();
        AddHandler(KeyDownEvent, LogSelection_KeyDown, RoutingStrategies.Tunnel);
        _logDragScrollTimer.Tick += (_, _) => ScrollLogSelection();
        Deactivated += (_, _) => FinishLogDrag();
        Closed += (_, _) => FinishLogDrag();
    }

    private void LogSelection_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse
            || e.GetCurrentPoint(LogLines).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed
            || e.Source is not Visual source) return;
        var ancestors = source.GetSelfAndVisualAncestors();
        if (ancestors.OfType<ListBoxItem>().FirstOrDefault()?.DataContext is not LogLineRow row) return;
        _searchNavigation++;
        ViewModel.Search.StartAt(row.Line);
        ClearLogTextRange();
        var text = ancestors.OfType<TextBox>().FirstOrDefault();
        ActivateLogText(text);
        if (e.ClickCount != 1 || text is null || GetLogTextPoint(text, e.GetPosition(this)) is not { } anchor) return;

        _logDragAnchor = anchor;
        _logDragPointer = e.Pointer;
        _logDragScroll = LogLines.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(scroll => ReferenceEquals(scroll.TemplatedParent, LogLines));
        _logDragViewport = _logDragScroll?.GetVisualDescendants().OfType<ScrollContentPresenter>()
            .FirstOrDefault(presenter => ReferenceEquals(presenter.TemplatedParent, _logDragScroll));
    }

    private void LogSelection_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _logDragPointer)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            FinishLogDrag();
            return;
        }

        _logDragPosition = e.GetPosition(this);
        if (ViewportEdgeDirection() == 0) _logDragScrollTimer.Stop();
        else _logDragScrollTimer.Start();
        if (UpdateLogDragRange()) e.Handled = true;
    }

    private void LogSelection_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _logDragPointer) || e.InitialPressMouseButton != MouseButton.Left) return;
        if (ReferenceEquals(e.Pointer.Captured, LogLines))
        {
            _logDragPosition = e.GetPosition(this);
            UpdateLogDragRange();
            e.Handled = true;
        }
        FinishLogDrag();
    }

    private void FinishLogDrag()
    {
        _logDragScrollTimer.Stop();
        var pointer = _logDragPointer;
        _logDragPointer = null;
        _logDragAnchor = default;
        _logDragScroll = null;
        _logDragViewport = null;
        if (pointer is not null && ReferenceEquals(pointer.Captured, LogLines)) pointer.Capture(null);
        // A drag that returns to its original row can use the native selection again.
        if (_logTextRange is { } range && ReferenceEquals(range.Start.Row, range.End.Row))
            _logTextRange = null;
    }

    private bool UpdateLogDragRange()
    {
        if (_logDragPointer is not { } pointer || _logDragViewport?.TranslatePoint(default, this) is not { } viewport)
            return false;
        var position = new Point(_logDragPosition.X,
            Math.Clamp(_logDragPosition.Y, viewport.Y, viewport.Y + _logDragViewport.Bounds.Height));
        if (FindLogTextPoint(position) is not { } end) return false;

        // Keep native click, drag and keyboard behavior within a row until we cross into another one.
        if (_logTextRange is null && ReferenceEquals(_logDragAnchor.Row, end.Row)) return false;
        var forward = _logDragAnchor.Row.Number < end.Row.Number
            || (ReferenceEquals(_logDragAnchor.Row, end.Row) && _logDragAnchor.Offset <= end.Offset);
        _logTextRange = forward ? (_logDragAnchor, end) : (end, _logDragAnchor);
        if (!ReferenceEquals(pointer.Captured, LogLines)) pointer.Capture(LogLines);
        RefreshLogTextRange();
        return true;
    }

    private LogTextPoint? FindLogTextPoint(Point position)
    {
        Control? closest = null;
        var distance = double.PositiveInfinity;
        foreach (var container in LogLines.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, this) is not { } top) continue;
            var bottom = top.Y + container.Bounds.Height;
            var gap = Math.Max(top.Y - position.Y, position.Y - bottom);
            if (gap <= 0) return LogTextPointInContainer(container, position);
            if (gap >= distance) continue;
            closest = container;
            distance = gap;
        }
        return closest is null ? null : LogTextPointInContainer(closest, position);
    }

    private LogTextPoint? LogTextPointInContainer(Control container, Point position) =>
        container.GetVisualDescendants().OfType<TextBox>().FirstOrDefault() is { } text
            ? GetLogTextPoint(text, position) : null;

    private LogTextPoint? GetLogTextPoint(TextBox text, Point position)
    {
        if (text.DataContext is not LogLineRow row
            || text.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { } presenter
            || this.TranslatePoint(position, presenter) is not { } local) return null;
        if (local.Y < 0) return new(row, 0);
        if (local.Y >= presenter.Bounds.Height) return new(row, row.Text.Length);
        var hit = presenter.TextLayout.HitTestPoint(local).CharacterHit;
        return new(row, Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, 0, row.Text.Length));
    }

    private int ViewportEdgeDirection()
    {
        if (_logDragViewport?.TranslatePoint(default, this) is not { } viewport) return 0;
        if (_logDragPosition.Y < viewport.Y) return -1;
        return _logDragPosition.Y >= viewport.Y + _logDragViewport.Bounds.Height ? 1 : 0;
    }

    private void ScrollLogSelection()
    {
        var direction = ViewportEdgeDirection();
        if (_logDragScroll is not { } scroll || direction == 0)
        {
            _logDragScrollTimer.Stop();
            return;
        }
        scroll.Offset = new Vector(scroll.Offset.X,
            Math.Clamp(scroll.Offset.Y + direction * 24, 0, scroll.ScrollBarMaximum.Y));
        LogLines.UpdateLayout();
        UpdateLogDragRange();
    }

    private bool TryGetLogTextRange(out int start, out int end)
    {
        start = end = -1;
        if (_logTextRange is not { } range) return false;
        var lines = DisplayedLogLines;
        start = LogSearch.IndexOfLine(lines, range.Start.Row.Line);
        end = LogSearch.IndexOfLine(lines, range.End.Row.Line);
        return start >= 0 && end >= start;
    }

    private bool IsInLogTextRange(LogLineRow row) =>
        _logTextRange is { } range && row.Number >= range.Start.Row.Number && row.Number <= range.End.Row.Number
        && TryGetLogTextRange(out _, out _);

    private IEnumerable<TextBox> RealizedLogTextBoxes() =>
        LogLines.GetRealizedContainers().SelectMany(container => container.GetVisualDescendants().OfType<TextBox>());

    private void RefreshLogTextRange()
    {
        if (_logTextRange is not { } range) return;
        if (!TryGetLogTextRange(out _, out _))
        {
            ClearLogTextRange();
            FinishLogDrag();
            return;
        }

        foreach (var text in RealizedLogTextBoxes())
        {
            if (text.DataContext is not LogLineRow row) continue;
            if (row.Number < range.Start.Row.Number || row.Number > range.End.Row.Number)
            {
                ClearLogTextSelection(text);
                continue;
            }
            var start = ReferenceEquals(row, range.Start.Row) ? range.Start.Offset : 0;
            var end = ReferenceEquals(row, range.End.Row) ? range.End.Offset : row.Text.Length;
            if (text.SelectionStart != start) text.SelectionStart = start;
            if (text.SelectionEnd != end) text.SelectionEnd = end;
        }
    }

    private void ClearLogTextRange()
    {
        if (_logTextRange is null) return;
        _logTextRange = null;
        foreach (var text in RealizedLogTextBoxes()) ClearLogTextSelection(text);
    }

    private string? GetLogTextRange()
    {
        if (_logTextRange is not { } range || !TryGetLogTextRange(out var start, out var end)) return null;
        var lines = DisplayedLogLines;
        var builder = new StringBuilder();
        for (var index = start; index <= end; index++)
        {
            if (index > start) builder.AppendLine();
            var line = lines[index].Text;
            var from = index == start ? range.Start.Offset : 0;
            var to = index == end ? range.End.Offset : line.Length;
            builder.Append(line.AsSpan(from, to - from));
        }
        return builder.ToString();
    }

    private async void LogSelection_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_logTextRange is null || e.Source is not Visual source
            || (!ReferenceEquals(source, this) && !source.GetSelfAndVisualAncestors().Contains(LogLines))) return;
        if (this.GetPlatformSettings()?.HotkeyConfiguration.Copy.Any(gesture => gesture.Matches(e)) == true)
        {
            e.Handled = true;
            await CopyLogTextAsync(GetLogTextRange());
        }
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            ClearLogTextRange();
        }
    }

    private async Task CopyLogTextAsync(string? text)
    {
        if (text is { Length: > 0 } && Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }
}
