#nullable enable
namespace Loupedeck.HapticAudioFeedback;

internal readonly record struct DetectionPeak(double AudioMilliseconds, double LevelDb, double ThresholdDb, string Reason, double Score);

// One fixed-duration candidate per band, never a queue. Deadline cannot slide with stronger peaks.
internal sealed class OnsetPeakPicker(double delayMilliseconds, double separationMilliseconds)
{
    private DetectionPeak? _pending;
    private double _deadline, _lastConfirmed = double.NegativeInfinity;
    public DetectionPeak? Update(double now, DetectionPeak? candidate)
    {
        if (now - _lastConfirmed < separationMilliseconds) return null;
        if (delayMilliseconds == 0)
        {
            if (candidate.HasValue) _lastConfirmed = now;
            return candidate;
        }
        if (!_pending.HasValue && candidate.HasValue) { _pending = candidate; _deadline = now + delayMilliseconds; }
        else if (candidate.HasValue && _pending.HasValue && candidate.Value.Score > _pending.Value.Score) _pending = candidate;
        if (!_pending.HasValue || now < _deadline) return null;
        var result = _pending;
        _pending = null; _lastConfirmed = now;
        return result;
    }
}
