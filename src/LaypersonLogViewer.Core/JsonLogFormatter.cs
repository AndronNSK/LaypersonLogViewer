using System.Text;
using System.Text.Json;

namespace LaypersonLogViewer.Core;

public sealed record FormattedLogText(string Text, int[] Offsets);

public static class JsonLogFormatter
{
    public static FormattedLogText? Format(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0 || text.Contains('\n') || text.Contains('\r')) return null;
        while (start >= 0)
        {
            try
            {
                // A timestamp prefix can itself contain brackets.
                using var document = JsonDocument.Parse(text[start..]);
                break;
            }
            catch (JsonException)
            {
                var next = text.AsSpan(start + 1).IndexOfAny('{', '[');
                start = next < 0 ? -1 : start + 1 + next;
            }
        }
        if (start < 0) return null;
        var prefixEnd = text.AsSpan(0, start).TrimEnd().Length;
        var output = new StringBuilder(text.Length + 64);
        var offsets = new int[text.Length + 1];
        var depth = 0;
        var quoted = false;
        var escaped = false;
        void NewLine()
        {
            output.Append('\n');
            output.Append(' ', depth * 2);
        }
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (index == start && prefixEnd > 0) NewLine();
            if (index >= start && !quoted && (c is '}' or ']'))
            {
                depth--;
                if (output.Length > 0 && output[^1] is not ('{' or '[')) NewLine();
            }
            offsets[index] = output.Length;
            if (index < start)
            {
                if (index < prefixEnd) output.Append(c);
                continue;
            }
            if (quoted)
            {
                output.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') quoted = false;
                continue;
            }
            if (char.IsWhiteSpace(c)) continue;
            output.Append(c);
            switch (c)
            {
                case '"': quoted = true; break;
                case '{':
                case '[':
                    depth++;
                    var next = index + 1;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && text[next] is not ('}' or ']')) NewLine();
                    break;
                case ',': NewLine(); break;
                case ':': output.Append(' '); break;
            }
        }
        offsets[text.Length] = output.Length;
        return new(output.ToString(), offsets);
    }
}
