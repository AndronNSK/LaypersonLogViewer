namespace LaypersonLogViewer.Core;

// Shared by background readers and the UI. Snapshots never expose the mutable queue.
public sealed class LiveLogBuffer(int capacity = 10000)
{
    private readonly object _gate = new();
    private readonly Queue<LogLine> _lines = new();
    private int _capacity = Math.Clamp(capacity, 100, 1000000);
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
            _capacity = Math.Clamp(capacity, 100, 1000000);
            Trim();
            _revision++;
        }
    }

    private void Trim() { while (_lines.Count > _capacity) _lines.Dequeue(); }

    public (LogLine[] Lines, int Received, long Revision) Snapshot()
    {
        lock (_gate) return (_lines.ToArray(), _received, _revision);
    }
}
