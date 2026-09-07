using Loupedeck.HapticAudioFeedback;

internal static class MonitorLifecycleChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var opens = 0;
        FakeCapture current = null;
        var settings = new AudioSettings { Enabled = false };
        using var monitor = new HapticAudioMonitor(new Loupedeck.Plugin(), settings, _ => { }, "", null, "",
            (_, _) => { Interlocked.Increment(ref opens); return current = new FakeCapture(); });
        monitor.Start();
        monitor.RestartCapture();
        monitor.UpdateSettings(s => s.CaptureDeviceId = "input:default");
        check(opens == 0 && monitor.GetMetrics().CaptureMode == "paused", "disabled startup, retry and source changes never open capture");
        monitor.UpdateSettings(s => s.Enabled = true);
        await Until(() => current?.Started == true);
        current.Emit();
        check(opens == 1 && monitor.GetMetrics().CapturePackets == 1 && monitor.GetMetrics().RecentAudio.Length > 0,
            "enabling opens capture and analyzes samples");
        var previous = current;
        monitor.UpdateSettings(s => s.Enabled = false);
        previous.Emit();
        check(previous.Disposed && previous.Stopped && monitor.GetMetrics().CapturePackets == 0 &&
            monitor.GetMetrics().RecentAudio.Length == 0 && monitor.GetMetrics().CaptureMode == "paused",
            "pause disposes capture and clears signal history; late callbacks cannot process");
        monitor.UpdateSettings(s => s.Enabled = true);
        await Until(() => opens == 2 && current.Started);
        check(!ReferenceEquals(previous, current), "resume opens a fresh capture");

        current.Fail();
        monitor.UpdateSettings(s => s.Enabled = false);
        await Task.Delay(750);
        check(opens == 2 && monitor.GetMetrics().CaptureError == null,
            "pause cancels device-failure recovery and clears stale errors");

        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new FakeCapture();
        using var pending = new HapticAudioMonitor(new Loupedeck.Plugin(), new AudioSettings(), _ => { }, "", null, "",
            (_, _) => { entered.SetResult(); release.Wait(TimeSpan.FromSeconds(5)); return late; });
        pending.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        pending.UpdateSettings(s => s.Enabled = false);
        release.Set();
        await Until(() => late.Disposed);
        check(!late.Started && pending.GetMetrics().CaptureMode == "paused", "pause cancels pending startup and disposes a late native result");
    }
    private static async Task Until(Func<bool> done)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!done()) await Task.Delay(10, timeout.Token);
    }
    private sealed class FakeCapture : ISystemAudioCapture
    {
        public volatile bool Started, Stopped, Disposed;
        public int SampleRate => 48000;
        public int Channels => 2;
        public string Mode => "test";
        public int RequestedBufferMilliseconds => 20;
        public event EventHandler<AudioCaptureData> DataAvailable;
        public event EventHandler<Exception> RecordingStopped;
        public void Fail() => RecordingStopped?.Invoke(this, new IOException("Device disconnected"));
        public void Emit() => DataAvailable?.Invoke(this, new AudioCaptureData(new float[1920]));
        public void StartRecording() => Started = true;
        public void StopRecording() => Stopped = true;
        public void Dispose() => Disposed = true;
    }
}
namespace Loupedeck
{
    internal sealed class Plugin { public TestEvents PluginEvents { get; } = new(); }
    internal sealed class TestEvents { public void RaiseEvent(string name) { } }
}
