namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;

/// <summary>Owns one helper process. Native driver code never runs inside the plugin host.</summary>
public sealed class CaptureHelperProcess : IDisposable
{
    private readonly Process _process;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task<string> _stderr;
    private int _disposed;
    public Stream Output { get; }
    public int ProcessId { get; }
    public CaptureHelperProcess(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardInput = start.RedirectStandardOutput = start.RedirectStandardError = true;
        _process = new Process { StartInfo = start };
        try
        {
            _process.Start();
            ProcessId = _process.Id;
            Output = _process.StandardOutput.BaseStream;
            _stderr = BoundedTextReader.DrainAsync(_process.StandardError, 4096, _stop.Token);
        }
        catch { _process.Dispose(); _stop.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        try
        {
            // Normal EOF shutdown first; a blocked driver is stopped by terminating our child.
            try { _process.StandardInput.Close(); }
            catch (IOException) { /* An exited helper may already have closed its pipe. */ }
            if (!_process.WaitForExit(250))
            {
                try { _process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (_process.HasExited) { }
                if (!_process.WaitForExit(1000)) throw new IOException("Capture helper did not terminate within the shutdown deadline.");
            }
        }
        finally
        {
            _process.Dispose();
            // Observe cancellation/failure so no abandoned stderr task can fault unobserved.
            _ = _stderr.ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, TaskScheduler.Default);
        }
    }
    public static AudioCaptureDevice[] ReadDevices(ProcessStartInfo start, TimeSpan timeout)
    {
        using var helper = new CaptureHelperProcess(start);
        using var deadline = new CancellationTokenSource(timeout);
        try { return ReadDevicesAsync(helper.Output, deadline.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { throw new TimeoutException("Audio device enumeration timed out. Reconnect the device and refresh the list."); }
    }
    private static async Task<AudioCaptureDevice[]> ReadDevicesAsync(Stream stream, CancellationToken cancellation)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > AudioDeviceCatalog.MaximumBytes) throw new IOException("Audio device catalog too large.");
            bytes.Write(buffer, 0, count);
        }
        return AudioDeviceCatalog.Decode(bytes.ToArray());
    }
}
