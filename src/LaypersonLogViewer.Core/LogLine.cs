namespace LaypersonLogViewer.Core;

// The number is the position in the original file, even after filtering.
public sealed record LogLine(int Number, string Text);
