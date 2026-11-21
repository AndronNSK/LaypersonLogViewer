namespace LaypersonLogViewer.Core;

// Shared by background readers and the UI. Snapshots never expose the mutable queue.
public sealed class LiveLogBuffer(int capacity = LiveLogBuffer.DefaultCapacity)
{
    public const int DefaultCapacity = 5_000_000;
    public const int MinimumCapacity = 100;
    public const int MaximumCapacity = 20_000_000;
    private readonly object _gate = new();
    private readonly Queue<LogLine> _lines = new();
    private int _capacity = Math.Clamp(capacity, MinimumCapacity, MaximumCapacity);
    private int _received;
    private long _revision;

    public void Add(string text)
    {
        lock (_gate)
        {
            _lines.Enqueue(new(++_received, text));
            Trim();
            _revision++;
        }
    }

    public void Resize(int capacity)
    {
        lock (_gate)
        {
            _capacity = Math.Clamp(capacity, MinimumCapacity, MaximumCapacity);
            Trim();
            _revision++;
        }
    }

    private void Trim() { while (_lines.Count > _capacity) _lines.Dequeue(); }

    public (LogLine[] Lines, int Received, long Revision) Snapshot()
    {
        lock (_gate) return (_lines.ToArray(), _received, _revision);
    }

    public (LogLine[]? Lines, int Received, int Count, long Revision) SnapshotAfter(long revision, bool includeLines)
    {
        lock (_gate)
            return (includeLines && _revision != revision ? _lines.ToArray() : null, _received, _lines.Count, _revision);
    }
}
