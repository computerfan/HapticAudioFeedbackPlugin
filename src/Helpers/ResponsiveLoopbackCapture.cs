namespace Loupedeck.HapticAudioFeedback;

using NAudio.CoreAudioApi;

/// <summary>Shared-mode loopback with a smaller buffer and audio-ready notifications.</summary>
public sealed class ResponsiveLoopbackCapture : WasapiCapture
{
    public const int RequestedBufferMilliseconds = 20;

    public ResponsiveLoopbackCapture(MMDevice device, bool useEventSync = true)
        : base(device, useEventSync, RequestedBufferMilliseconds) { }

    public static ResponsiveLoopbackCapture Create(bool useEventSync = true)
    {
        using var devices = new MMDeviceEnumerator();
        using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new ResponsiveLoopbackCapture(device, useEventSync);
    }

    protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
        AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
}
