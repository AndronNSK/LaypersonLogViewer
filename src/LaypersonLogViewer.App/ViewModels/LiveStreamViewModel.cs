using System.ComponentModel;
using LaypersonLogViewer.Core;

namespace LaypersonLogViewer.App.ViewModels;

public sealed class LiveStreamViewModel : ObservableModel, IDisposable
{
    private sealed class Session(string name, int limit)
    {
        public string Name { get; } = name;
        public LiveLogBuffer Buffer { get; } = new(limit);
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Work { get; set; } = Task.CompletedTask;
        public string Status = "Приём данных…";
    }
    private Session? _session;
    private bool _paused, _follow = true;
    private int _limit = 10000;
    private long _displayedRevision = -1;
    public bool HasSession => _session is not null;
    public bool IsActive => _session is { Work.IsCompleted: false };
    public Task Completion => _session?.Work ?? Task.CompletedTask;
    public bool IsPaused { get => _paused; set { _paused = value; Notify(); } }
    public bool FollowLatest { get => _follow; set { _follow = value; Notify(); } }
    public int RetainedLines
    {
        get => _limit;
        set { _limit = Math.Clamp(value, 100, 1000000); _session?.Buffer.Resize(_limit); Notify(); }
    }
    public string Status { get; private set; } = "";

    public void Start(string name, Func<Action<string>, CancellationToken, Task<string>> source)
    {
        Stop();
        var session = new Session(name, _limit);
        _session = session;
        _displayedRevision = -1;
        _paused = false;
        session.Work = Task.Run(async () =>
        {
            try { session.Status = await source(session.Buffer.Add, session.Cancellation.Token); }
            catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
            { session.Status = "Остановлено"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                     or Win32Exception or InvalidOperationException or NotSupportedException)
            { session.Status = "Ошибка источника: " + e.Message; }
            finally { session.Cancellation.Dispose(); }
        });
        Notify();
    }

    public async Task<bool> RefreshAsync(MainWindowViewModel model)
    {
        var session = _session;
        if (session is null) return false;
        var snapshot = session.Buffer.Snapshot();
        Status = $"{session.Status} · Получено: {snapshot.Received:N0} · В памяти: {snapshot.Lines.Length:N0}"
            + (IsPaused ? " · Показ приостановлен" : "");
        Notify();
        if (IsPaused || model.IsBusy || snapshot.Revision == _displayedRevision) return false;
        await model.ApplyLiveLinesAsync(session.Name, snapshot.Lines);
        if (ReferenceEquals(session, _session)) _displayedRevision = snapshot.Revision;
        return true;
    }

    public LogLine[] Snapshot() => _session?.Buffer.Snapshot().Lines ?? [];
    public void Stop()
    {
        if (_session is { } session)
        {
            try { session.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        Notify();
    }
    public void Detach() { Stop(); _session = null; Status = ""; Notify(); }
    public void Dispose() => Stop();
}
