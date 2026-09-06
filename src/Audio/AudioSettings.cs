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

    public bool Enabled { get; set; } = true;
    public double Sensitivity { get; set; } = 50;
    public double BassGainDb { get; set; } = 0;
    public double HighGainDb { get; set; } = 0;
    public bool BassEnabled { get; set; } = true;
    public bool HighEnabled { get; set; } = true;
    public double LowCenterHz { get; set; } = 100;
    public double HighCenterHz { get; set; } = 2000;
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
    public AudioSettings Copy() => (AudioSettings)MemberwiseClone();

    [System.Text.Json.Serialization.JsonIgnore]
    public double SensitivityGainDb => (Sensitivity - 50) * 0.24;
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveLowThresholdDb => LowThresholdDb - SensitivityGainDb - BassGainDb;
    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveHighThresholdDb => HighThresholdDb - SensitivityGainDb - HighGainDb;

    public static AudioSettings Profile(string name) => name switch
    {
        "music" => new AudioSettings { HighGainDb = -3, MinimumSpacingMilliseconds = 90 },
        "bass" => new AudioSettings { HighEnabled = false, BassGainDb = 3, MinimumSpacingMilliseconds = 100 },
        "gentle" => new AudioSettings { Sensitivity = 35, HighGainDb = -6, MinimumSpacingMilliseconds = 140,
            BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision" },
        _ => throw new ArgumentException("Unknown profile.")
    };
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
        Range(Sensitivity, 0, 100, nameof(Sensitivity));
        Range(BassGainDb, -12, 12, nameof(BassGainDb));
        Range(HighGainDb, -12, 12, nameof(HighGainDb));
        Range(LowCenterHz, 60, 250, nameof(LowCenterHz));
        Range(HighCenterHz, 800, 6000, nameof(HighCenterHz));
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
        if (BackgroundMilliseconds <= Math.Max(AttackMilliseconds, ReleaseMilliseconds))
            throw new ArgumentException("BackgroundMilliseconds must exceed attack and release.");
    }
}
