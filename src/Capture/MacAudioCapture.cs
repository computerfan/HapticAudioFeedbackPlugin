namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Sockets;

/// <summary>Uses the bundled CPAL helper app so audio permission belongs to its own bundle.</summary>
public sealed class MacAudioCapture : ISystemAudioCapture
{
    private readonly Process _process;
    private readonly BinaryReader _reader;
    private readonly CancellationTokenSource _stop = new();
    private Task _pump;
    private Socket _connection;
    private string _sessionDirectory;
    private int _disposed;
    private readonly CpalHelperProtocol _protocol;
    public int SampleRate { get; }
    public int Channels { get; }
    public string Mode { get; }
    public int RequestedBufferMilliseconds => 20;
    public event EventHandler<AudioCaptureData> DataAvailable;
    public event EventHandler<Exception> RecordingStopped;
    public MacAudioCapture(string pluginBinaryDirectory, string deviceId = "", CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("macOS capture is only available on macOS.");
        var executable = HelperPath(pluginBinaryDirectory);
        var bundle = Directory.GetParent(executable)!.Parent!.Parent!.FullName;
        // LaunchServices gives the helper its own application identity for TCC.
        // No TCP listener or PCM files: the socket lives in a private, short /tmp directory.
        _process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/open") {
            UseShellExecute = false, CreateNoWindow = true } };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sessionDirectory = "/tmp/ftr-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(_sessionDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var socketPath = Path.Combine(_sessionDirectory, "audio.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            listener.Listen(1);
            foreach (var argument in new[] { "-n", "-a", bundle, "--args", "--socket", socketPath, "--device", deviceId })
                _process.StartInfo.ArgumentList.Add(argument);
            _process.Start();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            _process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            if (_process.ExitCode != 0) throw new IOException("macOS could not launch the capture app. Open Feel the Rhythm Capture.app once in Finder and check any launch message.");
            _connection = listener.AcceptAsync(deadline.Token).AsTask().GetAwaiter().GetResult();
            // Connected Unix sockets no longer need their filesystem names.
            // Remove them before a potentially long permission wait or host crash.
            listener.Dispose();
            CleanupSessionDirectory();
            _reader = new BinaryReader(new NetworkStream(_connection, ownsSocket: false));
            // The socket connects before Core Audio asks for permission. Give the person time
            // to answer, independently of the launch timeout and the plugin's Load callback.
            using var permissionDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            permissionDeadline.CancelAfter(TimeSpan.FromMinutes(5));
            try { _protocol = CpalHelperProtocol.ReadHandshakeAsync(_reader.BaseStream, permissionDeadline.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for audio capture. Respond to the macOS permission prompt, then retry capture in settings.");
            }
            SampleRate = _protocol.SampleRate;
            Channels = _protocol.Channels;
            Mode = "CPAL CoreAudio app via LaunchServices" + (_protocol.DefaultBuffer ? " (device-default buffer)" : " (20 ms target)");
        }
        catch (Exception ex)
        {
            Dispose();
            throw new IOException("Could not start audio capture. " + ex.Message, ex);
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
        var start = new ProcessStartInfo(HelperPath(pluginBinaryDirectory));
        start.ArgumentList.Add("--list-devices");
        return CaptureHelperProcess.ReadDevices(start, TimeSpan.FromSeconds(10));
    }
    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_pump != null) throw new InvalidOperationException("Create a new capture instance to restart.");
        _pump = Task.Run(Pump);
    }
    private async Task Pump()
    {

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var packet = await _protocol.ReadPacketAsync(_reader.BaseStream, () => (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds, _stop.Token).ConfigureAwait(false);
                if (packet != null && !_stop.IsCancellationRequested) DataAvailable?.Invoke(this, packet);
            }
        }
        catch (Exception ex)
        {
            if (!_stop.IsCancellationRequested)
            {
                RecordingStopped?.Invoke(this, new IOException("Capture app disconnected: " + ex.Message, ex));
            }
        }
    }
    public void StopRecording() => Dispose();

    private void CleanupSessionDirectory()
    {
        if (_sessionDirectory == null) return;
        try
        {
            File.Delete(Path.Combine(_sessionDirectory, "audio.sock"));
            Directory.Delete(_sessionDirectory);
            _sessionDirectory = null;
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        // This process is only /usr/bin/open. The actual app observes socket EOF
        // and its native watchdog exits even if Core Audio is still awaiting permission.
        try { if (!_process.HasExited) _process.Kill(); } catch (InvalidOperationException) { }
        try { _connection?.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
        _connection?.Dispose();
        _reader?.Dispose();
        _process.Dispose();
        CleanupSessionDirectory();
        // Socket cancellation interrupts reads; never join a callback that could be blocked.
        _ = (_pump ?? Task.CompletedTask).ContinueWith(task => { _ = task.Exception; _stop.Dispose(); }, TaskScheduler.Default);
    }
}
