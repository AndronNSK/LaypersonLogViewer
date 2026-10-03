namespace LaypersonLogViewer.Core;

/// <summary>A fixed-position timestamp shape inferred from selected text.</summary>
public sealed class TimestampPattern
{
    public string Example { get; }
    public int StartIndex { get; }
    public string Shape => new(Example.Select(c => char.IsAsciiDigit(c) ? '#' : c).ToArray());

    public TimestampPattern(string example, int startIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(example);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        if (!example.Any(char.IsAsciiDigit) || example.Contains('\r') || example.Contains('\n'))
            throw new ArgumentException("Select a timestamp containing digits within one line.", nameof(example));
        Example = example;
        StartIndex = startIndex;
    }

    public bool Matches(string line)
    {
        if (StartIndex > line.Length || Example.Length > line.Length - StartIndex) return false;
        for (var i = 0; i < Example.Length; i++)
        {
            var actual = line[StartIndex + i];
            if (char.IsAsciiDigit(Example[i]) ? !char.IsAsciiDigit(actual) : actual != Example[i])
                return false;
        }
        return true;
    }
}
