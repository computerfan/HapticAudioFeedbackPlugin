using Loupedeck.HapticAudioFeedback;
internal static class CaptureRecoveryChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        check(CaptureRecovery.IsDefaultSource("") && CaptureRecovery.IsDefaultSource("input:default") &&
            !CaptureRecovery.IsDefaultSource("output:WASAPI:headphones"), "automatic recovery follows defaults but preserves explicit device selection");
        check(CaptureRecovery.NeedsRestart("", "", false, true, false) &&
            !CaptureRecovery.NeedsRestart("", "", true, false, false) &&
            !CaptureRecovery.NeedsRestart("", "", true, true, false) &&
            CaptureRecovery.NeedsRestart("", "input:default", true, true, true),
            "re-enabling haptics restarts capture while pause and tuning alone do not");
        check(!CaptureRecovery.NeedsRestart("", "", false, true, true) &&
            CaptureRecovery.NeedsRestart("", "input:default", false, true, true),
            "resume preserves a receiving stream; changing the source still restarts it");
        check(!CaptureRecovery.NeedsRestart("", "input:default", false, false, false) &&
            !CaptureRecovery.NeedsRestart("", "input:default", true, false, true),
            "changing source while disabled never opens capture");
        var gate = new object();
        var calls = 0;
        var firstRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var recovery = new CaptureRecovery(gate, () => {
            Interlocked.Increment(ref calls);
            firstRecovery.TrySetResult();
        }, ex => firstRecovery.TrySetException(ex), new[] { 30 }))
        {
            // A single burst must finish before its timer is allowed to fire.
            // Slow runners can otherwise split this loop into multiple valid recoveries.
            lock (gate) { for (var i=0;i<100;i++) recovery.Schedule(); }
            await firstRecovery.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(calls == 1, "device-change burst schedules exactly one recovery");
            lock (gate) { recovery.Schedule(); recovery.Cancel(); }
            await Task.Delay(100);
            check(calls == 1, "manual source change cancels pending recovery");
            lock (gate) { recovery.Schedule(); recovery.Dispose(); }
            await Task.Delay(100);
            check(calls == 1, "plugin unload cancels pending recovery");
        }
        var attempts = new List<long>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        CaptureRecovery retry = null;
        retry = new CaptureRecovery(new object(), () => {
            attempts.Add(clock.ElapsedMilliseconds);
            if (attempts.Count < 4) retry.Schedule(); else done.SetResult();
        }, ex => done.SetException(ex), new[] { 20, 60, 100 });
        using (retry) { retry.Schedule(); await done.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        check(attempts.Count == 4 && attempts[1]-attempts[0]>=40 && attempts[2]-attempts[1]>=80 &&
            attempts[3]-attempts[2]>=80, "unavailable default device retries with capped backoff");
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var recovery = new CaptureRecovery(new object(), () => recovered.TrySetResult(), _ => {}, new[] { 20, 2000 }))
        {
            recovery.Schedule(); await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            recovery.ResetBackoff(); recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            recovery.Schedule(); await recovered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            check(true, "receiving audio resets recovery delay for the next device switch");
        }
    }
}
