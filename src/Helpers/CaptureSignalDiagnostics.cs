namespace Loupedeck.HapticAudioFeedback;

// Updated under the monitor lock. Counters are saturated and serialized as strings.
internal sealed class CaptureSignalDiagnostics
{
    public long Packets { get; private set; }
    public long Samples { get; private set; }
    public DateTime? LastPacketUtc { get; private set; }
    public DateTime? LastSignalUtc { get; private set; }
    public double PeakDb { get; private set; } = -180;
    public void Observe(ReadOnlySpan<float> samples, DateTime now)
    {
        if (samples.IsEmpty) return;
        double peak = 0;
        foreach (var value in samples)
            if (float.IsFinite(value)) peak = Math.Max(peak, Math.Abs((double)value));
        Packets = SaturatingCounter.Add(Packets, 1);
        Samples = SaturatingCounter.Add(Samples, samples.Length);
        LastPacketUtc = now;
        PeakDb = peak > 0 ? Math.Max(-180, 20 * Math.Log10(peak)) : -180;
        if (peak > 0.000001) LastSignalUtc = now; // -120 dBFS; not the haptic trigger threshold.
    }
}
