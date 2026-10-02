namespace LaypersonLogViewer.Core;

public enum FilterKind { Include, Exclude }

public sealed class LogFilter
{
    public FilterKind Kind { get; }
    public string Text { get; }
    public bool CaseSensitive { get; }
    public string KindLabel => Kind == FilterKind.Include ? "Показать" : "Скрыть";
    public string CaseLabel => CaseSensitive ? "С учётом регистра" : "Без учёта регистра";

    public LogFilter(FilterKind kind, string text, bool caseSensitive = false)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Kind = kind;
        Text = text; // Preserve intentional spaces around the search term.
        CaseSensitive = caseSensitive;
    }

    public bool Matches(string line) => line.Contains(Text,
        CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
