namespace Loupedeck.HapticAudioFeedback;

/// <summary>Creates capture off the caller thread and publishes it under the owner's lifecycle lock.</summary>
public sealed class CaptureStartup
{
    private readonly object _gate;
    private CancellationTokenSource _pending;
    public CaptureStartup(object lifecycleGate) => _gate = lifecycleGate;
    public bool IsPending { get { lock (_gate) return _pending != null; } }

    public Task Start(Func<CancellationToken, ISystemAudioCapture> create,
        Action<ISystemAudioCapture> publish, Action<Exception> failed, Action<Exception> cleanupFailed)
    {
        lock (_gate)
        {
            Cancel();
            var pending = _pending = new CancellationTokenSource();
            return Task.Run(() =>
            {
                ISystemAudioCapture capture = null;
                try
                {
                    pending.Token.ThrowIfCancellationRequested();
                    capture = create(pending.Token);
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_pending, pending) || pending.IsCancellationRequested) return;
                        publish(capture);
                        capture = null; // Ownership transfers only after successful publication.
                    }
                }
                catch (Exception ex)
                {
                    lock (_gate)
                        if (ReferenceEquals(_pending, pending) && !pending.IsCancellationRequested) failed(ex);
                }
                finally
                {
                    try { capture?.Dispose(); }
                    catch (Exception ex) { cleanupFailed(ex); }
                    finally
                    {
                        lock (_gate)
                        {
                            if (ReferenceEquals(_pending, pending)) _pending = null;
                            pending.Dispose();
                        }
                    }
                }
            });
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            var pending = _pending;
            _pending = null;
            pending?.Cancel();
        }
    }
}
