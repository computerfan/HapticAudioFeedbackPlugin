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
    private Thread _thread;
    private Socket _connection;
    private string _sessionDirectory;
    private bool _disposed;
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
        using var process = new Process { StartInfo = new ProcessStartInfo(HelperPath(pluginBinaryDirectory)) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--list-devices");
        process.Start();
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var stderr = BoundedTextReader.DrainAsync(process.StandardError, 4096, timeout.Token);
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
                RecordingStopped?.Invoke(this, new IOException("Capture app disconnected: " + ex.Message, ex));
            }
        }
    }
    public void StopRecording()
    {
        _stop.Cancel();
        try { if (!_process.HasExited) _process.Kill(); } catch (InvalidOperationException) { }

        // EOF stops the helper; closing also unblocks a pending PCM read.
        try { _connection?.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
        _connection?.Dispose();
        if (_thread?.IsAlive == true && Thread.CurrentThread != _thread) _thread.Join();
        _reader?.Dispose();
        if (_sessionDirectory != null)
        {
            try { File.Delete(Path.Combine(_sessionDirectory, "audio.sock")); Directory.Delete(_sessionDirectory); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
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
