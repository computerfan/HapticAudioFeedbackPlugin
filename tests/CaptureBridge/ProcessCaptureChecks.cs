
using System.Diagnostics;
using System.Buffers.Binary;
using Loupedeck.HapticAudioFeedback;

internal static class ProcessCaptureChecks
{
    public static async Task RunHelper(string[] args)
    {
        File.WriteAllText(args[2], Environment.ProcessId.ToString());
        if (args[1] is "stream" or "packet") {
            var header = new byte[24]; "HCP1"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 48000);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 1920);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 20);
            var output = Console.OpenStandardOutput();
            await output.WriteAsync(header); await output.FlushAsync();
            if (args[1] == "packet") {
                using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true);
                writer.Write(2u); writer.Write(0u); writer.Write(0ul);
                writer.Write((DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
                writer.Write(.5f); writer.Write(-.5f); writer.Flush();
                return;
            }
        }
        // Intentionally ignore stdin EOF to simulate a native driver which never returns.
        await Task.Delay(Timeout.Infinite);
    }
    private static ProcessStartInfo Command(string mode, string pidFile)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!);
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ProcessCaptureChecks).Assembly.Location);
        foreach (var argument in new[] { "--test-helper", mode, pidFile }) start.ArgumentList.Add(argument);
        return start;
    }
    public static async Task Run(Action<bool, string> check)
    {
        var pidFile = Path.Combine(Path.GetTempPath(), "ftr-helper-test-" + Guid.NewGuid().ToString("N"));
        bool Exited() {
            if (!File.Exists(pidFile)) return false;
            try { using var child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile))); return child.HasExited; }
            catch (ArgumentException) { return true; }
        }
        try {
            var timer = Stopwatch.StartNew();
            try { using var capture = new HelperProcessAudioCapture(Command("hang", pidFile), TimeSpan.FromSeconds(1)); throw new Exception("Hang accepted"); }
            catch (TimeoutException) { check(timer.Elapsed < TimeSpan.FromSeconds(5) && Exited(), "startup deadline terminates a stuck helper"); }
            File.Delete(pidFile); timer.Restart();
            try { CaptureHelperProcess.ReadDevices(Command("hang", pidFile), TimeSpan.FromSeconds(1)); throw new Exception("Hang accepted"); }
            catch (TimeoutException) { check(timer.Elapsed < TimeSpan.FromSeconds(5) && Exited(), "enumeration deadline terminates a stuck helper"); }
            File.Delete(pidFile); timer.Restart();
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1))) {
                try { using var capture = new HelperProcessAudioCapture(Command("hang", pidFile), TimeSpan.FromSeconds(10), cancellation.Token); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { check(timer.Elapsed < TimeSpan.FromSeconds(5) && Exited(), "startup cancellation terminates a stuck helper"); }
            }
            File.Delete(pidFile);
            using (var capture = new HelperProcessAudioCapture(Command("stream", pidFile), TimeSpan.FromSeconds(5))) {
                capture.StartRecording(); await Task.Delay(100); timer.Restart();
                capture.StopRecording();
                check(timer.Elapsed < TimeSpan.FromSeconds(3) && Exited(), "shutdown interrupts an idle PCM read and kills a stuck helper");
                capture.Dispose();
            }
            File.Delete(pidFile);
            using (var capture = new HelperProcessAudioCapture(Command("packet", pidFile), TimeSpan.FromSeconds(5))) {
                var received = new TaskCompletionSource<float>(TaskCreationOptions.RunContinuationsAsynchronously);
                var stopped = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                capture.DataAvailable += (_, data) => received.TrySetResult(data.Samples.Span[0]);
                capture.RecordingStopped += (_, error) => stopped.TrySetResult(error);
                capture.StartRecording();
                check(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)) == .5f, "capture can restart and deliver PCM after timed-out attempts");
                check(await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)) is IOException, "unexpected helper exit reports a capture failure");
            }
        }
        finally { File.Delete(pidFile); }
    }
}
