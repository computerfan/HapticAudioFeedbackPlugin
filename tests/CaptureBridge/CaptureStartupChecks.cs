using Loupedeck.HapticAudioFeedback;

internal static class CaptureStartupChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var gate = new object();
        var startup = new CaptureStartup(gate);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var stale = new FakeCapture();
        ISystemAudioCapture published = null;
        var errors = 0;
        var cleanupErrors = 0;
        var slow = startup.Start(token => {
            entered.SetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return stale; // Simulate a native operation that completes after cancellation.
        }, capture => published = capture, _ => errors++, _ => cleanupErrors++);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            check(!slow.IsCompleted && startup.IsPending, "permission wait does not block startup caller");
            await Task.Run(() => { lock (gate) { } }).WaitAsync(TimeSpan.FromSeconds(5));
            check(true, "settings lifecycle lock stays available while permission is pending");
            var current = new FakeCapture();
            await startup.Start(_ => current, capture => published = capture, _ => errors++, _ => cleanupErrors++)
                .WaitAsync(TimeSpan.FromSeconds(5));
            check(ReferenceEquals(published, current) && !current.Disposed, "retry publishes the new capture and transfers ownership");
            // A stale cleanup failure must not invoke the startup error handler for the new capture.
            stale.ThrowOnDispose = true;
            release.Set();
            await slow.WaitAsync(TimeSpan.FromSeconds(5));
            check(stale.Disposed && ReferenceEquals(published, current) && errors == 0 && cleanupErrors == 1,
                "cancelled late capture is disposed without replacing or stopping the newer capture");
            current.Dispose();
        }
        finally { release.Set(); startup.Cancel(); }

        var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = startup.Start(token => {
            waiting.SetResult(true);
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
            return new FakeCapture();
        }, _ => throw new Exception("Cancelled capture published"), _ => errors++, _ => cleanupErrors++);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        startup.Cancel();
        await cancelled.WaitAsync(TimeSpan.FromSeconds(5));
        check(!startup.IsPending && errors == 0, "unload cancels a pending permission wait without reporting capture failure");
        await startup.Start(_ => throw new IOException("permission denied"), _ => { }, _ => errors++, _ => cleanupErrors++);
        check(errors == 1 && !startup.IsPending, "startup failure is reported and leaves retry available");
    }

    private sealed class FakeCapture : ISystemAudioCapture
    {
        public bool Disposed, ThrowOnDispose;
        public int SampleRate => 48000;
        public int Channels => 2;
        public string Mode => "test";
        public int RequestedBufferMilliseconds => 20;
        public event EventHandler<AudioCaptureData> DataAvailable { add { } remove { } }
        public event EventHandler<Exception> RecordingStopped { add { } remove { } }
        public void StartRecording() { }
        public void StopRecording() { }
        public void Dispose() { Disposed = true; if (ThrowOnDispose) throw new IOException("cleanup error"); }
    }
}
