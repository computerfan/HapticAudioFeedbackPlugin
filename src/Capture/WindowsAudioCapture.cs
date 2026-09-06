namespace Loupedeck.HapticAudioFeedback;

using NAudio.Wave;
using System.Runtime.InteropServices;

/// <summary>Existing Windows fallback. Not instantiated on macOS.</summary>
public sealed class WindowsAudioCapture : ISystemAudioCapture
{
    private readonly ResponsiveLoopbackCapture _capture;
    public int SampleRate => _capture.WaveFormat.SampleRate;
    public int Channels => _capture.WaveFormat.Channels;
    public string Mode { get; }
    public int RequestedBufferMilliseconds => ResponsiveLoopbackCapture.RequestedBufferMilliseconds;
    public event EventHandler<AudioCaptureData> DataAvailable;
    public event EventHandler<Exception> RecordingStopped;
    public WindowsAudioCapture(bool useEvents)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WASAPI fallback requires Windows.");
        _capture = ResponsiveLoopbackCapture.Create(useEvents);
        try
        {
            var format = _capture.WaveFormat;
            var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            if (!isFloat || format.BitsPerSample != 32 || format.BlockAlign != Channels * sizeof(float))
                throw new NotSupportedException("Expected interleaved float32 Windows audio.");
            Mode = useEvents ? "NAudio event-driven fallback" : "NAudio polling fallback";
            _capture.DataAvailable += (_, e) => {
                if (e.BytesRecorded % format.BlockAlign != 0) { RecordingStopped?.Invoke(this, new IOException("Incomplete audio frame.")); return; }
                var samples = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded)).ToArray();
                DataAvailable?.Invoke(this, new AudioCaptureData(samples));
            };
            _capture.RecordingStopped += (_, e) => { if (e.Exception != null) RecordingStopped?.Invoke(this, e.Exception); };
        }
        catch { _capture.Dispose(); throw; }
    }
    public void StartRecording() => _capture.StartRecording();
    public void StopRecording() => _capture.StopRecording();
    public void Dispose() => _capture.Dispose();
}