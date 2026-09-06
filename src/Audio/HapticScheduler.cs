namespace Loupedeck.HapticAudioFeedback;

/// <summary>One global playback gate. A callback contributes its strongest fresh onset; no backlog.</summary>
internal sealed class HapticScheduler
{
    private AudioSettings _settings;
    private double _lastSentMs = double.NegativeInfinity;
    public double? LastEventAgeMilliseconds { get; private set; }
    public long SentCount { get; private set; }
    public long DroppedCount { get; private set; }
    public HapticScheduler(AudioSettings settings) => _settings = settings;

    public void UpdateSettings(AudioSettings settings) => _settings = settings;

    public HapticOnset? Dispatch(IReadOnlyList<HapticOnset> candidates, double audioNowMs,
        double monotonicNowMs, Action<string> send)
    {
        HapticOnset? best = null;
        foreach (var onset in candidates)
        {
            var age = audioNowMs - onset.AudioMilliseconds;
            if (age < 0 || age > _settings.MaximumEventAgeMilliseconds) continue;
            if (!best.HasValue || (best.Value.IsSustain && !onset.IsSustain) ||
                (best.Value.IsSustain == onset.IsSustain && onset.StrengthDb > best.Value.StrengthDb)) best = onset;
        }
        if (!best.HasValue || monotonicNowMs - _lastSentMs < _settings.MinimumSpacingMilliseconds) {
            DroppedCount = SaturatingCounter.Add(DroppedCount, candidates.Count);
            return null;
        }
        // Reserve the slot even if the backend throws, avoiding a tight retry loop.
        _lastSentMs = monotonicNowMs;
        LastEventAgeMilliseconds = audioNowMs - best.Value.AudioMilliseconds;
        try { send(best.Value.EventName); }
        catch { DroppedCount = SaturatingCounter.Add(DroppedCount, candidates.Count); throw; }
        SentCount = SaturatingCounter.Add(SentCount, 1);
        DroppedCount = SaturatingCounter.Add(DroppedCount, candidates.Count - 1);
        return best;
    }
}
