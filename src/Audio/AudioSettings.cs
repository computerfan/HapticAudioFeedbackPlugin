namespace Loupedeck.HapticAudioFeedback;

internal sealed class AudioSettings
{
    public double LowThresholdDb { get; set; } = -38;
    public double HighThresholdDb { get; set; } = -42;
    public double AttackMilliseconds { get; set; } = 5;
    public double ReleaseMilliseconds { get; set; } = 60;
    public double BackgroundMilliseconds { get; set; } = 300;
    public double OnsetMarginDb { get; set; } = 6;
    public double RearmMarginDb { get; set; } = 2;
    public double MinimumSpacingMilliseconds { get; set; } = 80;
    public double MaximumEventAgeMilliseconds { get; set; } = 50;
    public double StrongBassAboveThresholdDb { get; set; } = 12;
    public bool EnableDebugServer { get; set; } = false;

    // Empty keeps the original default playback loopback. Direction is explicit for other choices.
    public string CaptureDeviceId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double Sensitivity { get; set; } = 50;
    public double BassGainDb { get; set; } = 0;
    public double HighGainDb { get; set; } = 0;
    public bool BassEnabled { get; set; } = true;
    public bool HighEnabled { get; set; } = true;
    public double LowCenterHz { get; set; } = 100;
    public double HighCenterHz { get; set; } = 2000;
    public double OnsetRiseWindowMilliseconds { get; set; } = 20;
    public double OnsetRiseDb { get; set; } = 3;
    public double TransientSeparationMilliseconds { get; set; } = 80;
    public string BassWaveform { get; set; } = "damp_collision";
    public string StrongBassWaveform { get; set; } = "sharp_collision";
    public string HighWaveform { get; set; } = "subtle_collision";
    public bool SustainEnabled { get; set; } = false;
    public string SustainWaveform { get; set; } = "subtle_collision";
    public double SustainThresholdDb { get; set; } = -30;
    public double SustainSlowIntervalMilliseconds { get; set; } = 260;
    public double SustainFastIntervalMilliseconds { get; set; } = 140;
    // Missing band overrides inherit legacy shared values, including old custom profiles.
    private double? _bassAttackMilliseconds;
    public double BassAttackMilliseconds { get => _bassAttackMilliseconds ?? AttackMilliseconds; set => _bassAttackMilliseconds = value; }
    private double? _bassReleaseMilliseconds;
    public double BassReleaseMilliseconds { get => _bassReleaseMilliseconds ?? ReleaseMilliseconds; set => _bassReleaseMilliseconds = value; }
    private double? _bassOnsetRiseWindowMilliseconds;
    public double BassOnsetRiseWindowMilliseconds { get => _bassOnsetRiseWindowMilliseconds ?? OnsetRiseWindowMilliseconds; set => _bassOnsetRiseWindowMilliseconds = value; }
    private double? _bassTransientSeparationMilliseconds;
    public double BassTransientSeparationMilliseconds { get => _bassTransientSeparationMilliseconds ?? TransientSeparationMilliseconds; set => _bassTransientSeparationMilliseconds = value; }
    public string BassTriggerMode { get; set; } = "both";
    private double? _highAttackMilliseconds;
    public double HighAttackMilliseconds { get => _highAttackMilliseconds ?? AttackMilliseconds; set => _highAttackMilliseconds = value; }
    private double? _highReleaseMilliseconds;
    public double HighReleaseMilliseconds { get => _highReleaseMilliseconds ?? ReleaseMilliseconds; set => _highReleaseMilliseconds = value; }
    private double? _highOnsetRiseWindowMilliseconds;
    public double HighOnsetRiseWindowMilliseconds { get => _highOnsetRiseWindowMilliseconds ?? OnsetRiseWindowMilliseconds; set => _highOnsetRiseWindowMilliseconds = value; }
    private double? _highTransientSeparationMilliseconds;
    public double HighTransientSeparationMilliseconds { get => _highTransientSeparationMilliseconds ?? TransientSeparationMilliseconds; set => _highTransientSeparationMilliseconds = value; }
    public string HighTriggerMode { get; set; } = "both";
    public double BassFilterQ { get; set; } = 1.2;
    public double HighFilterQ { get; set; } = 1.6;
    public string BassDetectionMethod { get; set; } = "envelope";
    public string HighDetectionMethod { get; set; } = "envelope";
    public double BassSpectralThreshold { get; set; } = 0.12;
    public double HighSpectralThreshold { get; set; } = 0.12;
    public AudioSettings Copy() => (AudioSettings)MemberwiseClone();

    [System.Text.Json.Serialization.JsonIgnore]
    public double SensitivityGainDb => (Sensitivity - 50) * 0.24;
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveLowThresholdDb => LowThresholdDb - SensitivityGainDb - BassGainDb;
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveHighThresholdDb => HighThresholdDb - SensitivityGainDb - HighGainDb;

    public static AudioSettings Profile(string name) => AudioProfiles.Create(name);

    public static AudioSettings Load(string assemblyFilePath, Action<Exception> onError)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(assemblyFilePath))
                throw new ArgumentException("The plugin host did not provide its assembly file path.");
            var assemblyDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(assemblyFilePath))
                ?? throw new ArgumentException("The host assembly path has no directory.");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyDirectory, "..", "audio-settings.json"));
            var settings = System.IO.File.Exists(path)
                ? System.Text.Json.JsonSerializer.Deserialize<AudioSettings>(System.IO.File.ReadAllText(path))
                    ?? throw new System.IO.InvalidDataException("Settings must be a JSON object.")
                : new AudioSettings();
            settings.Validate();
            return settings;
        }
        catch (Exception ex)
        {
            onError(ex);
            return new AudioSettings();
        }
    }
    public void Validate()
    {
        static void Range(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, $"Must be between {min} and {max}.");
        }
        if (CaptureDeviceId == null || System.Text.Encoding.UTF8.GetByteCount(CaptureDeviceId) > 4096 || CaptureDeviceId.Any(char.IsControl) ||
            (CaptureDeviceId.Length != 0 && !((CaptureDeviceId.StartsWith("input:", StringComparison.Ordinal) && CaptureDeviceId.Length > 6) ||
                (CaptureDeviceId.StartsWith("output:", StringComparison.Ordinal) && CaptureDeviceId.Length > 7))))
            throw new ArgumentException("Invalid audio device ID.");
        Range(BassAttackMilliseconds, 1, 100, nameof(BassAttackMilliseconds));
        Range(BassReleaseMilliseconds, 5, 1000, nameof(BassReleaseMilliseconds));
        Range(BassOnsetRiseWindowMilliseconds, 5, 100, nameof(BassOnsetRiseWindowMilliseconds));
        Range(BassTransientSeparationMilliseconds, 40, 500, nameof(BassTransientSeparationMilliseconds));
        Range(BassFilterQ, 0.5, 4, nameof(BassFilterQ));
        if (BassTriggerMode is not ("both" or "level" or "rise")) throw new ArgumentException("Unsupported trigger mode.");
        Range(HighAttackMilliseconds, 1, 100, nameof(HighAttackMilliseconds));
        Range(HighReleaseMilliseconds, 5, 1000, nameof(HighReleaseMilliseconds));
        Range(HighOnsetRiseWindowMilliseconds, 5, 100, nameof(HighOnsetRiseWindowMilliseconds));
        Range(HighTransientSeparationMilliseconds, 40, 500, nameof(HighTransientSeparationMilliseconds));
        Range(HighFilterQ, 0.5, 4, nameof(HighFilterQ));
        if (HighTriggerMode is not ("both" or "level" or "rise")) throw new ArgumentException("Unsupported trigger mode.");
        foreach (var method in new[] { BassDetectionMethod, HighDetectionMethod })
            if (method is not ("envelope" or "spectral")) throw new ArgumentException("Unsupported detection method.");
        Range(BassSpectralThreshold, .01, 1, nameof(BassSpectralThreshold));
        Range(HighSpectralThreshold, .01, 1, nameof(HighSpectralThreshold));
        Range(Sensitivity, 0, 100, nameof(Sensitivity));
        Range(BassGainDb, -12, 12, nameof(BassGainDb));
        Range(HighGainDb, -12, 12, nameof(HighGainDb));
        Range(LowCenterHz, 60, 250, nameof(LowCenterHz));
        Range(HighCenterHz, 800, 6000, nameof(HighCenterHz));
        Range(OnsetRiseWindowMilliseconds, 5, 100, nameof(OnsetRiseWindowMilliseconds));
        Range(OnsetRiseDb, 1, 12, nameof(OnsetRiseDb));
        Range(TransientSeparationMilliseconds, 40, 500, nameof(TransientSeparationMilliseconds));
        Range(SustainThresholdDb, -60, -6, nameof(SustainThresholdDb));
        Range(SustainFastIntervalMilliseconds, 100, 1000, nameof(SustainFastIntervalMilliseconds));
        Range(SustainSlowIntervalMilliseconds, SustainFastIntervalMilliseconds, 1500, nameof(SustainSlowIntervalMilliseconds));
        foreach (var waveform in new[] { BassWaveform, StrongBassWaveform, HighWaveform, SustainWaveform })
            if (waveform == null || !HapticPatterns.Presets.ContainsKey(waveform))
                throw new ArgumentException("Unsupported waveform.");
        if (SustainWaveform is not ("subtle_collision" or "damp_collision"))
            throw new ArgumentException("Sustained texture uses subtle_collision or damp_collision.");
        Range(LowThresholdDb, -90, 0, nameof(LowThresholdDb));
        Range(HighThresholdDb, -90, 0, nameof(HighThresholdDb));
        Range(AttackMilliseconds, 1, 100, nameof(AttackMilliseconds));
        Range(ReleaseMilliseconds, 5, 1000, nameof(ReleaseMilliseconds));
        Range(BackgroundMilliseconds, 100, 5000, nameof(BackgroundMilliseconds));
        Range(OnsetMarginDb, 1, 30, nameof(OnsetMarginDb));
        Range(RearmMarginDb, 0, OnsetMarginDb - 0.1, nameof(RearmMarginDb));
        Range(MinimumSpacingMilliseconds, 30, 1000, nameof(MinimumSpacingMilliseconds));
        Range(MaximumEventAgeMilliseconds, 5, 250, nameof(MaximumEventAgeMilliseconds));
        Range(StrongBassAboveThresholdDb, 0, 60, nameof(StrongBassAboveThresholdDb));
        if (BackgroundMilliseconds <= new[] { AttackMilliseconds, ReleaseMilliseconds, BassAttackMilliseconds, BassReleaseMilliseconds, HighAttackMilliseconds, HighReleaseMilliseconds }.Max())
            throw new ArgumentException("BackgroundMilliseconds must exceed attack and release.");
    }
}
