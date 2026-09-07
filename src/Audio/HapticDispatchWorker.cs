#nullable enable

namespace Loupedeck.HapticAudioFeedback;

/// <summary>One background worker, one accepted call, no backlog and no wait for a blocked SDK on disposal.</summary>
internal sealed class HapticDispatchWorker : IDisposable
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action<Exception> _failed;
    private Action? _work;
    private bool _busy, _stopped;

    public HapticDispatchWorker(Action<Exception> failed)
    {
        _failed = failed;
        var thread = new Thread(Run) { IsBackground = true, Name = "Feel the Rhythm haptic dispatch" };
        try { thread.Start(); }
        catch { _wake.Dispose(); throw; }
    }
    public bool TrySubmit(Action work)
    {
        lock (_gate)
        {
            if (_stopped || _busy) return false;
            _busy = true;
            _work = work;
            _wake.Set();
            return true;
        }
    }
    private void Run()
    {
        try
        {
            while (true)
            {
                _wake.WaitOne();
                Action? work;
                lock (_gate)
                {
                    if (_stopped) return;
                    work = _work;
                    _work = null;
                }
                try { work?.Invoke(); }
                catch (Exception ex) { try { _failed(ex); } catch { /* Keep the worker alive if diagnostics fail. */ } }
                finally { lock (_gate) _busy = false; }
            }
        }
        finally { _wake.Dispose(); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            _work = null;
            _wake.Set();
        }
        // The SDK provides no cancellation. A running call may finish later; never spawn a replacement.
    }
}
