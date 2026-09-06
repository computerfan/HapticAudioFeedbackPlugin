namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Uses the bundled CPAL helper app so audio permission belongs to its own bundle.</summary>
public sealed class MacAudioCapture : ISystemAudioCapture
{
    private readonly Process _process;
    private readonly BinaryReader _reader;
    private readonly CancellationTokenSource _stop = new();
    private Thread _thread;
    private bool _disposed;
    private readonly CpalHelperProtocol _protocol;
    public int SampleRate { get; }
    public int Channels { get; }
    public string Mode { get; }
    public int RequestedBufferMilliseconds => 20;
    public event EventHandler<AudioCaptureData> DataAvailable;
    public event EventHandler<Exception> RecordingStopped;
    public MacAudioCapture(string pluginBinaryDirectory, string deviceId = "")
    {
        var executable = HelperPath(pluginBinaryDirectory);
        _process = new Process { StartInfo = new ProcessStartInfo(executable) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true } };
        _process.StartInfo.ArgumentList.Add("--device");
        _process.StartInfo.ArgumentList.Add(deviceId);
        try
        {
            _process.Start();
            _reader = new BinaryReader(_process.StandardOutput.BaseStream);
            var header = new byte[24];
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _reader.BaseStream.ReadExactlyAsync(header, deadline.Token).AsTask().GetAwaiter().GetResult();
            _protocol = new CpalHelperProtocol(header);
            SampleRate = _protocol.SampleRate;
            Channels = _protocol.Channels;
            Mode = "CPAL CoreAudio helper" + (_protocol.DefaultBuffer ? " (device-default buffer)" : " (20 ms target)");
        }
        catch (Exception ex)
        {
            string detail = null;
            if (HasExited()) detail = _process.StandardError.ReadToEnd();
            Dispose();
            throw new IOException("Could not start system audio capture. " + (string.IsNullOrWhiteSpace(detail) ? ex.Message : detail.Trim()), ex);
        }
    }
    private static string HelperPath(string pluginBinaryDirectory)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(14, 6)) throw new PlatformNotSupportedException("CPAL system audio capture requires macOS 14.6 or later.");
        if (string.IsNullOrWhiteSpace(pluginBinaryDirectory) || !Path.IsPathFullyQualified(pluginBinaryDirectory)) throw new ArgumentException("An absolute plugin binary directory is required.");
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : throw new PlatformNotSupportedException();
        return Path.Combine(pluginBinaryDirectory, "runtimes", "osx-" + arch, "native", "Feel the Rhythm Capture.app", "Contents", "MacOS", "haptic-cpal-helper");
    }
    public static AudioCaptureDevice[] ListDevices(string pluginBinaryDirectory)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(HelperPath(pluginBinaryDirectory)) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--list-devices");
        process.Start();
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = process.StandardOutput.BaseStream.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult()) > 0) {
                if (bytes.Length + count > AudioDeviceCatalog.MaximumBytes) throw new IOException("Audio device catalog too large.");
                bytes.Write(buffer, 0, count);
            }
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            var error = stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new IOException(error.Trim());
            return AudioDeviceCatalog.Decode(bytes.ToArray());
        } finally {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
        }
    }
    private bool HasExited() { try { return _process.HasExited; } catch { return false; } }
    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != null) throw new InvalidOperationException("Create a new capture instance to restart.");
        _thread = new Thread(Pump) { IsBackground = true, Name = "HapticMacCpalConsumer" };
        _thread.Start();
    }
    private void Pump()
    {

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = _protocol.ReadPacket(_reader.BaseStream, () => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
                if (packet != null) DataAvailable?.Invoke(this, packet);
            }
        }
        catch (Exception ex)
        {
            if (!_stop.IsCancellationRequested)
            {
                var detail = HasExited() ? _process.StandardError.ReadToEnd().Trim() : null;
                RecordingStopped?.Invoke(this, new IOException(string.IsNullOrWhiteSpace(detail) ? ex.Message : detail, ex));
            }
        }
    }
    public void StopRecording()
    {
        _stop.Cancel();
        try { _process.StandardInput.Close(); } catch (InvalidOperationException) { }
        try
        {
            if (!_process.WaitForExit(1500)) { _process.Kill(); _process.WaitForExit(); }
        }
        catch (InvalidOperationException) { }
        if (_thread?.IsAlive == true && Thread.CurrentThread != _thread) _thread.Join();
        _reader?.Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return;
        StopRecording();
        _process.Dispose();
        _stop.Dispose();
        _disposed = true;
    }
}