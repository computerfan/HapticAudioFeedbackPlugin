namespace Loupedeck.HapticAudioFeedback;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>Dedicated native owner thread; loads only an absolute packaged library path.</summary>
public sealed class CpalAudioCapture : ISystemAudioCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Format { public uint SampleRate, Channels, Capacity, RequestedMs, DefaultBuffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Packet { public uint Samples, Discontinuity; public ulong DroppedFrames; public double NewestAgeMs; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Open([In] byte[] key, uint keyLength, out Format format, [Out] byte[] error, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Read(IntPtr handle, [Out] float[] samples, uint capacity, out Packet packet, [Out] byte[] error, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out] byte[] output, uint capacity, [Out] byte[] error, uint length);
    private readonly byte[] _deviceKey;
    private IntPtr _library;
    private Open _open;
    private Read _read;
    private Close _close;
    private Format _format;
    private Exception _initializationError;
    private readonly ManualResetEventSlim _ready = new(false), _start = new(false), _stop = new(false);
    private Thread _thread;
    private bool _disposed, _started;
    public int SampleRate => (int)_format.SampleRate;
    public int Channels => (int)_format.Channels;
    public string Mode => "CPAL WASAPI" + (_format.DefaultBuffer != 0 ? " (device-default buffer)" : " (20 ms target)");
    public int RequestedBufferMilliseconds => 20;
    public event EventHandler<AudioCaptureData> DataAvailable;
    public event EventHandler<Exception> RecordingStopped;

    public static string LibraryRelativePath()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The in-process CPAL adapter is for Windows. macOS uses its permission-owning helper.");
        var arch = RuntimeInformation.ProcessArchitecture switch {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => throw new PlatformNotSupportedException("Capture requires a 64-bit host.") };
        return Path.Combine("runtimes", "win-" + arch, "native", "haptic_cpal.dll");
    }
    public CpalAudioCapture(string pluginBinaryDirectory, string deviceId = "")
    {
        if (string.IsNullOrWhiteSpace(pluginBinaryDirectory) || !Path.IsPathFullyQualified(pluginBinaryDirectory))
            throw new ArgumentException("An absolute plugin binary directory is required.");
        _deviceKey = Encoding.UTF8.GetBytes(deviceId ?? throw new ArgumentNullException(nameof(deviceId)));
        try
        {
            _library = NativeLibrary.Load(Path.Combine(pluginBinaryDirectory, LibraryRelativePath()));
            T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            if (Export<AbiVersion>("haptic_cpal_abi_version")() != 2) throw new InvalidOperationException("CPAL binding version mismatch. Reinstall the plugin package.");
            _open = Export<Open>("haptic_cpal_open_device");
            _read = Export<Read>("haptic_cpal_read");
            _close = Export<Close>("haptic_cpal_close");
            _thread = new Thread(Pump) { IsBackground = true, Name = "HapticCpalOwner" };
            _thread.Start();
            _ready.Wait();
            if (_initializationError != null) throw new IOException(_initializationError.Message, _initializationError);
        }
        catch { Dispose(); throw; }
    }
    public static AudioCaptureDevice[] ListDevices(string pluginBinaryDirectory)
    {
        if (!Path.IsPathFullyQualified(pluginBinaryDirectory)) throw new ArgumentException("An absolute plugin directory is required.");
        AudioCaptureDevice[] devices = null; Exception failure = null;
        var library = NativeLibrary.Load(Path.Combine(pluginBinaryDirectory, LibraryRelativePath()));
        try {
            var abi = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library, "haptic_cpal_abi_version"));
            if (abi() != 2) throw new IOException("CPAL binding version mismatch. Reinstall the plugin package.");
            var enumerate = Marshal.GetDelegateForFunctionPointer<Enumerate>(NativeLibrary.GetExport(library, "haptic_cpal_devices"));
            var owner = new Thread(() => {
                try {
                    var bytes = new byte[AudioDeviceCatalog.MaximumBytes]; var error = new byte[2048];
                    var count = enumerate(bytes, (uint)bytes.Length, error, (uint)error.Length);
                    if (count < 0) throw CapturePermissionException.FromNativeMessage(Decode(error));
                    if (count > bytes.Length) throw new IOException("Invalid device catalog length.");
                    devices = AudioDeviceCatalog.Decode(bytes.AsSpan(0, count).ToArray());
                } catch (Exception ex) { failure = ex; }
            }) { IsBackground = true, Name = "HapticCpalDevices" };
            owner.Start(); owner.Join();
        } finally {
            // CPAL's COM thread-local destructors must run before native code is unloaded.
            NativeLibrary.Free(library);
        }
        if (failure != null) throw new IOException("Could not list audio devices. " + failure.Message, failure);
        return devices;
    }
    private static string Decode(byte[] text)
    {
        var end = Array.IndexOf(text, (byte)0);
        return Encoding.UTF8.GetString(text, 0, end < 0 ? text.Length : end);
    }
    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) throw new InvalidOperationException("Create a new capture instance to restart.");
        _started = true;
        _start.Set();
    }
    private void Pump()
    {
        IntPtr handle = IntPtr.Zero;
        var initialized = false;
        Exception stopped = null;
        try
        {
            var error = new byte[2048];
            // COM initialization, stream creation, reads and destruction all use this owner thread.
            handle = _open(_deviceKey, (uint)_deviceKey.Length, out _format, error, (uint)error.Length);
            if (handle == IntPtr.Zero) throw CapturePermissionException.FromNativeMessage(Decode(error));
            if (_format.SampleRate is < 8000 or > 384000 || _format.Channels is < 1 or > 32 ||
                _format.Capacity == 0 || _format.Capacity > 384000 * 32 || _format.Capacity % _format.Channels != 0)
                throw new IOException("Invalid CPAL audio format.");
            var samples = new float[_format.Capacity];
            initialized = true;
            _ready.Set();
            _start.Wait();
            while (!_stop.IsSet)
            {
                var result = _read(handle, samples, (uint)samples.Length, out var packet, error, (uint)error.Length);
                if (result < 0) throw CapturePermissionException.FromNativeMessage(Decode(error));
                if (result > 0 && !_stop.IsSet)
                {
                    if (packet.Samples > samples.Length || packet.Samples % Channels != 0 ||
                        !double.IsFinite(packet.NewestAgeMs) || packet.NewestAgeMs < 0)
                        throw new IOException("Invalid CPAL audio packet.");
                    DataAvailable?.Invoke(this, new AudioCaptureData(samples.AsMemory(0, (int)packet.Samples),
                        packet.NewestAgeMs, packet.Discontinuity != 0, packet.DroppedFrames));
                }
                // Native read waits at most 20 ms for the next packet; no polling sleep.
            }
        }
        catch (Exception ex) { if (!initialized) _initializationError = ex; else stopped = ex; }
        finally
        {
            if (handle != IntPtr.Zero) _close(handle);
            _ready.Set();
        }
        if (stopped != null && !_stop.IsSet) RecordingStopped?.Invoke(this, stopped);
    }
    public void StopRecording()
    {
        _stop.Set();
        _start.Set();
        if (_thread?.IsAlive == true)
        {
            if (Thread.CurrentThread == _thread) throw new InvalidOperationException("Stop capture outside its callback thread.");
            _thread.Join();
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        StopRecording();
        // The owner and all CPAL threads have exited before native code can be unloaded.
        if (_library != IntPtr.Zero) { NativeLibrary.Free(_library); _library = IntPtr.Zero; }
        _ready.Dispose(); _start.Dispose(); _stop.Dispose();
        _disposed = true;
    }
}