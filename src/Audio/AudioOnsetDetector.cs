#nullable enable

namespace Loupedeck.HapticAudioFeedback;

using NAudio.Dsp;

internal readonly record struct HapticOnset(string EventName, double StrengthDb, double AudioMilliseconds, bool IsSustain = false, string Band = "unknown", double? LevelDb = null, string? TriggerReason = null);
internal readonly record struct DetectorReading(double AudioMilliseconds, BandReading Low, BandReading High);
internal readonly record struct BandReading(double EnvelopeDb, double BackgroundDb, double ThresholdDb, bool Onset, string? TriggerReason = null, DetectionPeak? Peak = null);

/// <summary>Per-channel filtering and fixed-duration energy windows. No device I/O.</summary>
internal sealed class AudioOnsetDetector
{
    private readonly BiQuadFilter[] _lowFilters, _highFilters;
    private readonly BandEnvelope _low, _high;
    private readonly SpectralFluxDetector? _spectral;
    private readonly AudioSettings _settings;
    private readonly int _sampleRate, _windowFrames;
    private readonly double _lowFilterGain, _highFilterGain;
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
        // Constant-skirt filters have peak gain Q; retain the legacy center gain as width changes.
        _lowFilterGain = 1.2 / settings.BassFilterQ;
        _highFilterGain = 1.6 / settings.HighFilterQ;
        _settings = settings;
        if (settings.BassDetectionMethod == "spectral" || settings.HighDetectionMethod == "spectral")
            _spectral = new SpectralFluxDetector(sampleRate, channels, settings);
        _windowFrames = Math.Max(1, (int)Math.Round(sampleRate * 0.005));
        _lowFilters = new BiQuadFilter[channels];
        _highFilters = new BiQuadFilter[channels];
        for (var c = 0; c < channels; c++)
        {
            _lowFilters[c] = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, (float)settings.LowCenterHz, (float)settings.BassFilterQ);
            _highFilters[c] = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, (float)Math.Min(settings.HighCenterHz, sampleRate * 0.4), (float)settings.HighFilterQ);
        }
        var windowMs = _windowFrames * 1000.0 / sampleRate;
        _low = new BandEnvelope(windowMs, settings.EffectiveLowThresholdDb, settings, true);
        _high = new BandEnvelope(windowMs, settings.EffectiveHighThresholdDb, settings, false);
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
                _spectral?.Add(c, input);
                var low = _lowFilters[c].Transform(input) * _lowFilterGain;
                var high = _highFilters[c].Transform(input) * _highFilterGain;
                _lowEnergy += (double)low * low;
                _highEnergy += (double)high * high;
            }
            _spectral?.Advance();
            _frames++;
            if (++_framesInWindow != _windowFrames) continue;
            Low = _low.Update(Math.Sqrt(_lowEnergy / (_windowFrames * Channels)), _spectral is { Ready: true } ? _spectral.Bass : null, AudioMilliseconds);
            High = _high.Update(Math.Sqrt(_highEnergy / (_windowFrames * Channels)), _spectral is { Ready: true } ? _spectral.High : null, AudioMilliseconds);
            _framesInWindow = 0;
            _lowEnergy = _highEnergy = 0;

            // Evaluate both bands before choosing the stronger relative onset.
            HapticOnset? candidate = null;
            if (_settings.Enabled && _settings.BassEnabled && Low.Onset)
            {
                var lowPeak = Low.Peak!.Value;
                var pattern = lowPeak.LevelDb - _settings.EffectiveLowThresholdDb >= _settings.StrongBassAboveThresholdDb
                    ? HapticPatterns.EventFor(_settings.StrongBassWaveform, "strong")
                    : HapticPatterns.EventFor(_settings.BassWaveform, "bass");
                _lastBassPulseMs = AudioMilliseconds;
                candidate = new HapticOnset(pattern, lowPeak.LevelDb - lowPeak.ThresholdDb, lowPeak.AudioMilliseconds, Band: "bass", LevelDb: lowPeak.LevelDb, TriggerReason: lowPeak.Reason);
            }
            if (_settings.Enabled && _settings.HighEnabled && High.Onset && (!candidate.HasValue || High.Peak!.Value.LevelDb - High.Peak.Value.ThresholdDb > candidate.Value.StrengthDb))
                candidate = new HapticOnset(HapticPatterns.EventFor(_settings.HighWaveform, "high"), High.Peak!.Value.LevelDb - High.Peak.Value.ThresholdDb, High.Peak.Value.AudioMilliseconds, Band: "high", LevelDb: High.Peak.Value.LevelDb, TriggerReason: High.Peak.Value.Reason);
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
        private double _fast, _background, _elapsedMs;
        private readonly double[] _history;
        private int _historyIndex;
        private readonly string _triggerMode;
        private readonly bool _useSpectral;
        private readonly double _spectralThreshold;
        private readonly OnsetPeakPicker _peaks;
        private bool _armed = true;
        public BandEnvelope(double windowMs, double floorDb, AudioSettings settings, bool bass)
        {
            _settings = settings;
            _useSpectral = (bass ? settings.BassDetectionMethod : settings.HighDetectionMethod) == "spectral";
            _spectralThreshold = bass ? settings.BassSpectralThreshold : settings.HighSpectralThreshold;
            _triggerMode = bass ? settings.BassTriggerMode : settings.HighTriggerMode;
            _peaks = new OnsetPeakPicker(bass ? settings.BassPeakConfirmationMilliseconds : settings.HighPeakConfirmationMilliseconds,
                bass ? settings.BassTransientSeparationMilliseconds : settings.HighTransientSeparationMilliseconds);
            _history = Enumerable.Repeat(-180.0, Math.Max(1, (int)Math.Round((bass ? settings.BassOnsetRiseWindowMilliseconds : settings.HighOnsetRiseWindowMilliseconds) / windowMs))).ToArray();
            _floorDb = floorDb;
            _attack = 1 - Math.Exp(-windowMs / (bass ? settings.BassAttackMilliseconds : settings.HighAttackMilliseconds));
            _release = 1 - Math.Exp(-windowMs / (bass ? settings.BassReleaseMilliseconds : settings.HighReleaseMilliseconds));
            _backgroundFollow = 1 - Math.Exp(-windowMs / settings.BackgroundMilliseconds);
        }
        public BandReading Update(double rms, double? flux, double audioMilliseconds)
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
            _elapsedMs = audioMilliseconds;
            // A new attack can emerge above sustained music before the slow threshold is crossed.
            var newAttack = _triggerMode != "level" && riseDb >= _settings.OnsetRiseDb &&
                envDb >= Math.Max(_floorDb, backgroundDb + _settings.RearmMarginDb);
            var levelTrigger = _triggerMode != "rise" && _armed && envDb >= thresholdDb;
            var spectralTrigger = flux >= _spectralThreshold && envDb >= _floorDb;
            var qualifies = _useSpectral ? spectralTrigger : (levelTrigger || newAttack);
            var reason = _useSpectral ? "spectral" : levelTrigger ? "threshold" : "rise";
            // Envelope peaks rank by level; spectral peaks rank by novelty. Preserve the measured frame.
            DetectionPeak? candidate = qualifies ? new(_elapsedMs, envDb, thresholdDb, reason, _useSpectral ? flux!.Value : envDb) : null;
            var peak = _peaks.Update(_elapsedMs, candidate);
            if (peak.HasValue) _armed = false;
            return new BandReading(envDb, backgroundDb, thresholdDb, peak.HasValue, peak?.Reason, peak);
        }
    }
}
