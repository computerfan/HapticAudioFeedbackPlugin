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
        var best = Reserve(candidates, audioNowMs, monotonicNowMs);
        if (!best.HasValue) return null;
        try { send(best.Value.EventName); }
        catch { Complete(false); throw; }
        Complete(true);
        return best;
    }

    // Caller serializes reservations/completions; the backend call must run outside that lock.
    public HapticOnset? Reserve(IReadOnlyList<HapticOnset> candidates, double audioNowMs, double monotonicNowMs)
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
        DroppedCount = SaturatingCounter.Add(DroppedCount, candidates.Count - 1);
        return best;
    }
    public void Complete(bool sent, double? completedMilliseconds = null)
    {
        if (sent) SentCount = SaturatingCounter.Add(SentCount, 1);
        else DroppedCount = SaturatingCounter.Add(DroppedCount, 1);
        if (completedMilliseconds.HasValue) _lastSentMs = Math.Max(_lastSentMs, completedMilliseconds.Value);
    }
}
