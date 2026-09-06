namespace Loupedeck.HapticAudioFeedback;

/// <summary>Interleaved float PCM. Samples are valid only during the synchronous callback.</summary>
public sealed record AudioCaptureData(ReadOnlyMemory<float> Samples, double NewestSampleAgeMs = 0,
    bool Discontinuity = false, ulong DroppedFrames = 0);
public interface ISystemAudioCapture : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    string Mode { get; }
    int RequestedBufferMilliseconds { get; }
    event EventHandler<AudioCaptureData> DataAvailable;
    event EventHandler<Exception> RecordingStopped;
    void StartRecording();
    void StopRecording();
}