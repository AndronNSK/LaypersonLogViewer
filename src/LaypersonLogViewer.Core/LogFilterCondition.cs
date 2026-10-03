namespace LaypersonLogViewer.Core;

public sealed class LogFilterCondition
{
    public string Text { get; }
    public bool CaseSensitive { get; }
    public string CaseLabel => CaseSensitive ? "С учётом регистра" : "Без учёта регистра";

    public LogFilterCondition(string text, bool caseSensitive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
        CaseSensitive = caseSensitive;
    }

    public bool Matches(string text) => text.Contains(Text,
        CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
