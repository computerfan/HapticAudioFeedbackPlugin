namespace Loupedeck.HapticAudioFeedback;

/// <summary>One cancellable retry timer, serialized with capture lifecycle changes.</summary>
public sealed class CaptureRecovery : IDisposable
{
    private readonly object _gate;
    private readonly Action _recover;
    private readonly Action<Exception> _failed;
    private readonly int[] _delays;
    private Timer _timer;
    private object _ticket;
    private int _attempt;
    private bool _disposed;
    public CaptureRecovery(object lifecycleGate, Action recover, Action<Exception> failed, int[] delays = null)
    {
        _gate = lifecycleGate; _recover = recover; _failed = failed;
        _delays = delays?.ToArray() ?? new[] { 500, 1000, 2000, 5000, 10000, 30000 };
        if (_delays.Length == 0 || _delays.Any(delay => delay <= 0)) throw new ArgumentException("Positive retry delays are required.");
    }
    public static bool NeedsRestart(string oldSource, string newSource, bool wasEnabled, bool enabled, bool receivingAudio)
        => oldSource != newSource || (!wasEnabled && enabled && !receivingAudio);
    public static bool IsDefaultSource(string id) => id is "" or "output:default" or "input:default";
    public void Schedule()
    {
        lock (_gate)
        {
            if (_disposed || _timer != null) return;
            var ticket = _ticket = new object();
            var delay = _delays[Math.Min(_attempt, _delays.Length - 1)];
            _attempt = Math.Min(_attempt + 1, _delays.Length - 1);
            _timer = new Timer(_ =>
            {
                lock (_gate)
                {
                    if (_disposed || !ReferenceEquals(ticket, _ticket)) return;
                    _timer.Dispose(); _timer = null; _ticket = null;
                    try { _recover(); } catch (Exception ex) { try { _failed(ex); } catch { } }
                }
            }, null, delay, Timeout.Infinite);
        }
    }
    public void ResetBackoff() => Interlocked.Exchange(ref _attempt, 0);
    public void Cancel(bool resetBackoff = true)
    {
        lock (_gate)
        {
            _ticket = null; _timer?.Dispose(); _timer = null;
            if (resetBackoff) _attempt = 0;
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; Cancel(); } }
}
