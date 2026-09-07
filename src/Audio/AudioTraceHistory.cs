#nullable enable

namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json.Serialization;

internal sealed record AudioTracePoint(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Sequence,
    DateTime Timestamp, double LowEnvDb, double HighEnvDb,
    double LowThresholdDb, double HighThresholdDb, bool BreakBefore, string? SentBand = null, string? TriggerReason = null, string? SentTexture = null);

// Envelope/threshold telemetry only; no audio samples. Accessed under the monitor lock.
internal sealed class AudioTraceHistory
{
    internal const int Capacity = 2560;
    internal const int SnapshotCapacity = 256;
    private readonly AudioTracePoint?[] _points = new AudioTracePoint?[Capacity];
    private readonly double[] _audioTimes = new double[Capacity];
    private int _next, _count;
    private long _sequence;
    private bool _breakBefore = true;

    internal void Clear()
    {
        Array.Clear(_points);
        _next = _count = 0;
        _breakBefore = true;
    }

    internal void Add(DateTime timestamp, double audioMilliseconds, double low, double high, double lowThreshold, double highThreshold)
    {
        if (!double.IsFinite(audioMilliseconds) || !double.IsFinite(low) || !double.IsFinite(high) ||
            !double.IsFinite(lowThreshold) || !double.IsFinite(highThreshold)) return;
        _sequence = SaturatingCounter.Add(_sequence, 1);
        _points[_next] = new(_sequence, timestamp, low, high, lowThreshold, highThreshold, _breakBefore);
        _audioTimes[_next] = audioMilliseconds;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
        _breakBefore = false;
    }

    internal bool MarkSent(double audioMilliseconds, string band, string? triggerReason = null, string? eventName = null)
    {
        if (band is not ("bass" or "high")) return false;
        for (var offset = 1; offset <= _count; offset++)
        {
            var index = (_next - offset + Capacity) % Capacity;
            if (_audioTimes[index] == audioMilliseconds)
            {
                _points[index] = _points[index]! with { SentBand = band, SentTexture = eventName == null ? null : HapticPatterns.WaveformForEvent(eventName), TriggerReason = triggerReason is "threshold" or "rise" ? triggerReason : null };
                return true;
            }
            if (_audioTimes[index] < audioMilliseconds) break;
        }
        return false;
    }

    // Overlapping short batches recover missed polls without resending 12 seconds at 10 Hz.
    internal AudioTracePoint[] Snapshot(DateTime now)
    {
        var points = new List<AudioTracePoint>(SnapshotCapacity);
        for (var offset = Math.Min(_count, SnapshotCapacity); offset > 0; offset--)
        {
            var point = _points[(_next - offset + Capacity) % Capacity]!;
            if ((now - point.Timestamp).TotalSeconds <= 12) points.Add(point);
        }
        return points.ToArray();
    }
}
