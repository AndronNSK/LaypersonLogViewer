using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using LaypersonLogViewer.App.ViewModels;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.Views;

// Draw highlights from the same text layout that handles wrapping, selection and hit testing.
public sealed class LogSearchHighlight : Control
{
    private static readonly IBrush TextHighlight = Brush.Parse("#55FFD23C");
    private static readonly IBrush MatchingNumber = Brush.Parse("#FFF0AF");
    private static readonly IBrush CurrentNumber = Brush.Parse("#FFD26E");

    public static readonly StyledProperty<TextBox?> TargetProperty =
        AvaloniaProperty.Register<LogSearchHighlight, TextBox?>(nameof(Target));
    public static readonly StyledProperty<LogLineRow?> RowProperty =
        AvaloniaProperty.Register<LogSearchHighlight, LogLineRow?>(nameof(Row));
    public static readonly StyledProperty<string> QueryProperty =
        AvaloniaProperty.Register<LogSearchHighlight, string>(nameof(Query), "");
    public static readonly StyledProperty<bool> CaseSensitiveProperty =
        AvaloniaProperty.Register<LogSearchHighlight, bool>(nameof(CaseSensitive));
    public static readonly StyledProperty<LogSearchMatch?> CurrentMatchProperty =
        AvaloniaProperty.Register<LogSearchHighlight, LogSearchMatch?>(nameof(CurrentMatch));
    public static readonly DirectProperty<LogSearchHighlight, IBrush?> NumberBackgroundProperty =
        AvaloniaProperty.RegisterDirect<LogSearchHighlight, IBrush?>(nameof(NumberBackground), control => control.NumberBackground);

    private (int Start, int Length)[] _matches = [];
    private IBrush? _numberBackground;
    private (TextLayout Layout, Point Origin)? _textGeometry;

    public LogSearchHighlight() => LayoutUpdated += (_, _) => UpdateTextGeometry();

    public TextBox? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public LogLineRow? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    public string Query { get => GetValue(QueryProperty); set => SetValue(QueryProperty, value); }
    public bool CaseSensitive { get => GetValue(CaseSensitiveProperty); set => SetValue(CaseSensitiveProperty, value); }
    public LogSearchMatch? CurrentMatch { get => GetValue(CurrentMatchProperty); set => SetValue(CurrentMatchProperty, value); }
    public IBrush? NumberBackground => _numberBackground;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowProperty || change.Property == QueryProperty || change.Property == CaseSensitiveProperty
            || change.Property == FormatJsonProperty)
        {
            _matches = Row is { } row && Query.Length > 0
                ? LogSearch.HighlightRanges(row.Text, Query, CaseSensitive).ToArray() : [];
            UpdateTextGeometry();
            InvalidateVisual();
            UpdateNumberBackground();
        }
        else if (change.Property == CurrentMatchProperty) UpdateNumberBackground();
        else if (change.Property == TargetProperty) UpdateTextGeometry();
    }

    private void UpdateNumberBackground()
    {
        var background = _matches.Length == 0 ? null
            : ReferenceEquals(CurrentMatch?.Line, Row?.Line) ? CurrentNumber : MatchingNumber;
        SetAndRaise(NumberBackgroundProperty, ref _numberBackground, background);
    }

    private void UpdateTextGeometry()
    {
        (TextLayout Layout, Point Origin)? geometry = null;
        if (_matches.Length > 0 && Target?.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter
            && presenter.TranslatePoint(default, this) is { } origin)
            geometry = (presenter.TextLayout, origin);
        if (_textGeometry == geometry) return;
        _textGeometry = geometry;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_textGeometry is not { } geometry) return;
        using var translation = context.PushTransform(Matrix.CreateTranslation(geometry.Origin.X, geometry.Origin.Y));
        foreach (var (start, length) in _matches)
            foreach (var rect in geometry.Layout.HitTestTextRange(
                Row!.DisplayOffset(start, FormatJson),
                Row.DisplayOffset(start + length, FormatJson) - Row.DisplayOffset(start, FormatJson)))
                context.FillRectangle(TextHighlight, rect);
    }

    public static readonly StyledProperty<bool> FormatJsonProperty =
        AvaloniaProperty.Register<LogSearchHighlight, bool>(nameof(FormatJson));
    public bool FormatJson { get => GetValue(FormatJsonProperty); set => SetValue(FormatJsonProperty, value); }
}
