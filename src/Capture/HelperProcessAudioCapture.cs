namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;

/// <summary>Bounded startup and cancellable PCM reads from an owned capture process.</summary>
public class HelperProcessAudioCapture : ISystemAudioCapture
{
    private readonly CaptureHelperProcess _helper;
    private readonly CpalHelperProtocol _protocol;
    private readonly CancellationTokenSource _stop = new();
    private Task _pump;
    private int _disposed;
    public int SampleRate => _protocol.SampleRate;
    public int Channels => _protocol.Channels;
    public string Mode => "CPAL WASAPI helper" + (_protocol.DefaultBuffer ? " (device-default buffer)" : " (20 ms target)");
    public int RequestedBufferMilliseconds => 20;
    public event EventHandler<AudioCaptureData> DataAvailable;
    public event EventHandler<Exception> RecordingStopped;
    public HelperProcessAudioCapture(ProcessStartInfo start, TimeSpan timeout, CancellationToken cancellation = default)
    {
        try { cancellation.ThrowIfCancellationRequested(); _helper = new CaptureHelperProcess(start); }
        catch { _stop.Dispose(); throw; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        try { _protocol = CpalHelperProtocol.ReadHandshakeAsync(_helper.Output, deadline.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { Dispose(); throw new TimeoutException("Audio capture startup timed out. Reconnect the device and retry capture."); }
        catch { Dispose(); throw; }
    }
    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pump != null) throw new InvalidOperationException("Create a new capture instance to restart.");
        _pump = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var data = await _protocol.ReadPacketAsync(_helper.Output,
                        () => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds, _stop.Token).ConfigureAwait(false);
                    if (data != null && !_stop.IsCancellationRequested) DataAvailable?.Invoke(this, data);
                }
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested) RecordingStopped?.Invoke(this, new IOException("Capture helper stopped: " + ex.Message, ex));
            }
        });
    }
    public void StopRecording() => Dispose();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try { _helper?.Dispose(); }
        finally
        {
            // Do not wait for application callbacks here; no native library can be unloaded underneath them.
            _ = (_pump ?? Task.CompletedTask).ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
