#nullable enable

namespace Loupedeck.HapticAudioFeedback;

using NAudio.Dsp;

internal readonly record struct HapticOnset(string EventName, double StrengthDb, double AudioMilliseconds, bool IsSustain = false, string Band = "unknown", double? LevelDb = null, string? TriggerReason = null);
internal readonly record struct DetectorReading(double AudioMilliseconds, BandReading Low, BandReading High);
internal readonly record struct BandReading(double EnvelopeDb, double BackgroundDb, double ThresholdDb, bool Onset, string? TriggerReason = null);

/// <summary>Per-channel filtering and fixed-duration energy windows. No device I/O.</summary>
internal sealed class AudioOnsetDetector
{
    private readonly BiQuadFilter[] _lowFilters, _highFilters;
    private readonly BandEnvelope _low, _high;
    private readonly AudioSettings _settings;
    private readonly int _sampleRate, _windowFrames;
    private int _framesInWindow;
    private long _frames;
    private double _lowEnergy, _highEnergy;
    private double _lastBassPulseMs, _sustainActiveSinceMs = -1;

    public int Channels => _lowFilters.Length;
    public double AudioMilliseconds => _frames * 1000.0 / _sampleRate;
    public double ReadingAudioMilliseconds => (_frames - _framesInWindow) * 1000.0 / _sampleRate;
    public BandReading Low { get; private set; }
    public BandReading High { get; private set; }

    public AudioOnsetDetector(int sampleRate, int channels, AudioSettings settings)
    {
        settings.Validate();
        if (sampleRate < 8000 || channels < 1 || channels > 32)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Unsupported sample rate or channel count.");
        _sampleRate = sampleRate;
        _settings = settings;
        _windowFrames = Math.Max(1, (int)Math.Round(sampleRate * 0.005));
        _lowFilters = new BiQuadFilter[channels];
        _highFilters = new BiQuadFilter[channels];
        for (var c = 0; c < channels; c++)
        {
            _lowFilters[c] = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, (float)settings.LowCenterHz, 1.2f);
            _highFilters[c] = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, (float)Math.Min(settings.HighCenterHz, sampleRate * 0.4), 1.6f);
        }
        var windowMs = _windowFrames * 1000.0 / sampleRate;
        _low = new BandEnvelope(windowMs, settings.EffectiveLowThresholdDb, settings);
        _high = new BandEnvelope(windowMs, settings.EffectiveHighThresholdDb, settings);
    }

    public void Process(ReadOnlySpan<float> interleaved, Action<HapticOnset> onOnset, Action<DetectorReading>? onReading = null)
    {
        if (interleaved.Length % Channels != 0)
            throw new ArgumentException("Audio must contain complete interleaved frames.");
        for (var i = 0; i < interleaved.Length; i += Channels)
        {
            for (var c = 0; c < Channels; c++)
            {
                var input = float.IsFinite(interleaved[i + c]) ? Math.Clamp(interleaved[i + c], -1f, 1f) : 0f;
                var low = _lowFilters[c].Transform(input);
                var high = _highFilters[c].Transform(input);
                _lowEnergy += (double)low * low;
                _highEnergy += (double)high * high;
            }
            _frames++;
            if (++_framesInWindow != _windowFrames) continue;
            Low = _low.Update(Math.Sqrt(_lowEnergy / (_windowFrames * Channels)));
            High = _high.Update(Math.Sqrt(_highEnergy / (_windowFrames * Channels)));
            _framesInWindow = 0;
            _lowEnergy = _highEnergy = 0;

            // Evaluate both bands before choosing the stronger relative onset.
            HapticOnset? candidate = null;
            if (_settings.Enabled && _settings.BassEnabled && Low.Onset)
            {
                var pattern = Low.EnvelopeDb - _settings.EffectiveLowThresholdDb >= _settings.StrongBassAboveThresholdDb
                    ? HapticPatterns.EventFor(_settings.StrongBassWaveform, "strong")
                    : HapticPatterns.EventFor(_settings.BassWaveform, "bass");
                _lastBassPulseMs = AudioMilliseconds;
                candidate = new HapticOnset(pattern, Low.EnvelopeDb - Low.ThresholdDb, AudioMilliseconds, Band: "bass", LevelDb: Low.EnvelopeDb, TriggerReason: Low.TriggerReason);
            }
            if (_settings.Enabled && _settings.HighEnabled && High.Onset && (!candidate.HasValue || High.EnvelopeDb - High.ThresholdDb > candidate.Value.StrengthDb))
                candidate = new HapticOnset(HapticPatterns.EventFor(_settings.HighWaveform, "high"), High.EnvelopeDb - High.ThresholdDb, AudioMilliseconds, Band: "high", LevelDb: High.EnvelopeDb, TriggerReason: High.TriggerReason);
            // Optional pulse-density texture. It never predicts beats or queues a repeating timer.
            var sustainFloor = _settings.SustainThresholdDb - _settings.SensitivityGainDb - _settings.BassGainDb;
            if (!_settings.Enabled || !_settings.BassEnabled || !_settings.SustainEnabled || Low.EnvelopeDb < sustainFloor)
                _sustainActiveSinceMs = -1;
            else
            {
                if (_sustainActiveSinceMs < 0) _sustainActiveSinceMs = AudioMilliseconds;
                var strength = Math.Clamp((Low.EnvelopeDb - sustainFloor) / 18, 0, 1);
                var interval = _settings.SustainSlowIntervalMilliseconds - strength *
                    (_settings.SustainSlowIntervalMilliseconds - _settings.SustainFastIntervalMilliseconds);
                if (!candidate.HasValue && AudioMilliseconds - _sustainActiveSinceMs >= 200 &&
                    AudioMilliseconds - _lastBassPulseMs >= interval)
                {
                    candidate = new HapticOnset(HapticPatterns.EventFor(_settings.SustainWaveform, "sustain"),
                        Low.EnvelopeDb - sustainFloor, AudioMilliseconds, true, "bass");
                    _lastBassPulseMs = AudioMilliseconds;
                }
            }
            onReading?.Invoke(new DetectorReading(AudioMilliseconds, Low, High));
            if (candidate.HasValue) onOnset(candidate.Value);
        }
    }

    private sealed class BandEnvelope
    {
        private readonly AudioSettings _settings;
        private readonly double _floorDb, _attack, _release, _backgroundFollow;
        private double _fast, _background, _elapsedMs, _lastOnsetMs = double.NegativeInfinity;
        private readonly double _windowMs;
        private readonly double[] _history;
        private int _historyIndex;
        private bool _armed = true;
        public BandEnvelope(double windowMs, double floorDb, AudioSettings settings)
        {
            _settings = settings;
            _windowMs = windowMs;
            _history = Enumerable.Repeat(-180.0, Math.Max(1, (int)Math.Round(settings.OnsetRiseWindowMilliseconds / windowMs))).ToArray();
            _floorDb = floorDb;
            _attack = 1 - Math.Exp(-windowMs / settings.AttackMilliseconds);
            _release = 1 - Math.Exp(-windowMs / settings.ReleaseMilliseconds);
            _backgroundFollow = 1 - Math.Exp(-windowMs / settings.BackgroundMilliseconds);
        }
        public BandReading Update(double rms)
        {
            _fast += (rms > _fast ? _attack : _release) * (rms - _fast);
            _background += _backgroundFollow * (rms - _background);
            var envDb = 20 * Math.Log10(Math.Max(_fast, 1e-9));
            var backgroundDb = 20 * Math.Log10(Math.Max(_background, 1e-9));
            var thresholdDb = Math.Max(_floorDb, backgroundDb + _settings.OnsetMarginDb);
            if (envDb < _floorDb - 3 || envDb - backgroundDb <= _settings.RearmMarginDb) _armed = true;
            var riseDb = envDb - _history[_historyIndex];
            _history[_historyIndex] = envDb;
            _historyIndex = (_historyIndex + 1) % _history.Length;
            _elapsedMs += _windowMs;
            // A new attack can emerge above sustained music before the slow threshold is crossed.
            var newAttack = riseDb >= _settings.OnsetRiseDb &&
                envDb >= Math.Max(_floorDb, backgroundDb + _settings.RearmMarginDb);
            var levelTrigger = _armed && envDb >= thresholdDb;
            var onset = (levelTrigger || newAttack) &&
                _elapsedMs - _lastOnsetMs >= _settings.TransientSeparationMilliseconds;
            if (onset) { _armed = false; _lastOnsetMs = _elapsedMs; }
            // If both rules qualify, report the level rule; rapid rise means it was needed.
            return new BandReading(envDb, backgroundDb, thresholdDb, onset,
                onset ? (levelTrigger ? "threshold" : "rise") : null);
        }
    }
}
