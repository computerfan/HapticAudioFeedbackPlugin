namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json.Serialization;

internal sealed record OnsetMarker(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Sequence,
    DateTime Timestamp, string Band, double LevelDb, double? ThresholdDb = null);

// Accessed under the monitor lock. UI telemetry never queues haptic playback.
internal sealed class OnsetHistory
{
    internal const int Capacity = 256;
    private readonly Queue<OnsetMarker> _events = new();
    private long _sequence;

    internal void Add(DateTime timestamp, string band, double levelDb, double? thresholdDb = null)
    {
        if (band is not ("bass" or "high") || !double.IsFinite(levelDb)) return;
        _sequence = SaturatingCounter.Add(_sequence, 1);
        while (_events.Count >= Capacity) _events.Dequeue();
        _events.Enqueue(new(_sequence, timestamp, band, Math.Clamp(levelDb, -180, 0),
            thresholdDb is { } threshold && double.IsFinite(threshold) ? threshold : null));
    }

    internal OnsetMarker[] Snapshot(DateTime now)
    {
        while (_events.TryPeek(out var item) && (now - item.Timestamp).TotalSeconds > 12) _events.Dequeue();
        return _events.ToArray();
    }
}
