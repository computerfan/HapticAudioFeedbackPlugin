namespace Loupedeck.HapticAudioFeedback;

internal static class AudioProfiles
{
    internal sealed record Definition(string Id, string Label, string Description, Func<AudioSettings> Create);

    public static readonly IReadOnlyList<Definition> All = Array.AsReadOnly(new Definition[]
    {
        new("spectral", "Spectral / experimental", "Experimental frequency-change detection for detail, with envelope bass and no confirmation delay.",
            () => new AudioSettings { HighDetectionMethod = "spectral", HighSpectralThreshold = .12,
                HighGainDb = -3, MinimumSpacingMilliseconds = 100, HighTransientSeparationMilliseconds = 100 }),
        new("music", "Music", "Balanced rhythm with rounded bass and quieter detail taps.",
            () => new AudioSettings {
                BassTriggerMode = "both", HighTriggerMode = "rise", BassFilterQ = 1.2, HighFilterQ = 1.6,
                BassAttackMilliseconds = 5, HighAttackMilliseconds = 3, BassReleaseMilliseconds = 60, HighReleaseMilliseconds = 40,
                BassOnsetRiseWindowMilliseconds = 25, HighOnsetRiseWindowMilliseconds = 15, BassTransientSeparationMilliseconds = 90, HighTransientSeparationMilliseconds = 80,
                HighGainDb = -3, MinimumSpacingMilliseconds = 90 }),
        new("bass", "Bass focus", "Bass attacks only, with extra low-band sensitivity.",
            () => new AudioSettings {
                BassTriggerMode = "both", HighTriggerMode = "both", BassFilterQ = 1.8, HighFilterQ = 1.6,
                BassAttackMilliseconds = 5, HighAttackMilliseconds = 5, BassReleaseMilliseconds = 70, HighReleaseMilliseconds = 60,
                BassOnsetRiseWindowMilliseconds = 30, HighOnsetRiseWindowMilliseconds = 20, BassTransientSeparationMilliseconds = 100, HighTransientSeparationMilliseconds = 80,
                HighEnabled = false, BassGainDb = 3, MinimumSpacingMilliseconds = 100 }),
        new("gentle", "Gentle", "Fewer, softer pulses for background listening.",
            () => new AudioSettings {
                BassTriggerMode = "level", HighTriggerMode = "level", BassFilterQ = 1.0, HighFilterQ = 1.4,
                BassAttackMilliseconds = 10, HighAttackMilliseconds = 8, BassReleaseMilliseconds = 100, HighReleaseMilliseconds = 80,
                BassOnsetRiseWindowMilliseconds = 35, HighOnsetRiseWindowMilliseconds = 25, BassTransientSeparationMilliseconds = 180, HighTransientSeparationMilliseconds = 200,
                Sensitivity = 35, HighGainDb = -6, MinimumSpacingMilliseconds = 140,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision" }),
        new("electronic", "Electronic / dance", "Emphasizes deep bass attacks and reduces bright detail; sustained texture stays off.",
            () => new AudioSettings {
                BassTriggerMode = "rise", HighTriggerMode = "rise", BassFilterQ = 1.8, HighFilterQ = 2.0,
                BassAttackMilliseconds = 3, HighAttackMilliseconds = 2, BassReleaseMilliseconds = 50, HighReleaseMilliseconds = 35,
                BassOnsetRiseWindowMilliseconds = 20, HighOnsetRiseWindowMilliseconds = 10, BassTransientSeparationMilliseconds = 100, HighTransientSeparationMilliseconds = 80,
                Sensitivity = 55, BassGainDb = 3, HighGainDb = -6,
                LowCenterHz = 80, HighCenterHz = 3200, MinimumSpacingMilliseconds = 110,
                TransientSeparationMilliseconds = 100, StrongBassAboveThresholdDb = 10 }),
        new("rock", "Rock / live", "More separation between impacts in dense mixes, with restrained upper-band taps.",
            () => new AudioSettings {
                BassTriggerMode = "both", HighTriggerMode = "rise", BassFilterQ = 1.2, HighFilterQ = 1.8,
                BassAttackMilliseconds = 5, HighAttackMilliseconds = 3, BassReleaseMilliseconds = 55, HighReleaseMilliseconds = 40,
                BassOnsetRiseWindowMilliseconds = 25, HighOnsetRiseWindowMilliseconds = 15, BassTransientSeparationMilliseconds = 120, HighTransientSeparationMilliseconds = 100,
                Sensitivity = 45, BassGainDb = 1, HighGainDb = -4,
                LowCenterHz = 120, HighCenterHz = 2500, MinimumSpacingMilliseconds = 120,
                TransientSeparationMilliseconds = 100, OnsetRiseDb = 4,
                ReleaseMilliseconds = 55, BackgroundMilliseconds = 350, StrongBassAboveThresholdDb = 15 }),
        new("acoustic", "Acoustic / jazz", "Softer taps and wider spacing for lighter arrangements; detail remains enabled.",
            () => new AudioSettings {
                BassTriggerMode = "both", HighTriggerMode = "both", BassFilterQ = 0.9, HighFilterQ = 1.2,
                BassAttackMilliseconds = 8, HighAttackMilliseconds = 5, BassReleaseMilliseconds = 90, HighReleaseMilliseconds = 65,
                BassOnsetRiseWindowMilliseconds = 40, HighOnsetRiseWindowMilliseconds = 25, BassTransientSeparationMilliseconds = 150, HighTransientSeparationMilliseconds = 120,
                Sensitivity = 55, BassGainDb = -2, HighGainDb = -2,
                LowCenterHz = 160, HighCenterHz = 1800, MinimumSpacingMilliseconds = 150,
                TransientSeparationMilliseconds = 140, OnsetRiseDb = 4, OnsetMarginDb = 8,
                AttackMilliseconds = 8, ReleaseMilliseconds = 90, BackgroundMilliseconds = 450,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision" }),
        new("cinema", "Movies / cinematic", "Sparse low-end impacts. Dialogue and soundtrack are not recognized separately.",
            () => new AudioSettings {
                BassTriggerMode = "both", HighTriggerMode = "level", BassFilterQ = 1.8, HighFilterQ = 1.6,
                BassAttackMilliseconds = 8, HighAttackMilliseconds = 5, BassReleaseMilliseconds = 100, HighReleaseMilliseconds = 60,
                BassOnsetRiseWindowMilliseconds = 50, HighOnsetRiseWindowMilliseconds = 20, BassTransientSeparationMilliseconds = 220, HighTransientSeparationMilliseconds = 200,
                Sensitivity = 35, BassGainDb = 4, HighEnabled = false,
                LowCenterHz = 70, LowThresholdDb = -32, MinimumSpacingMilliseconds = 220,
                TransientSeparationMilliseconds = 200, StrongBassAboveThresholdDb = 18,
                OnsetRiseDb = 5, ReleaseMilliseconds = 100, BackgroundMilliseconds = 600 }),
        new("games", "Games / action", "Separated, higher-threshold impacts. Reacts to game audio; does not identify gameplay events.",
            () => new AudioSettings {
                BassTriggerMode = "rise", HighTriggerMode = "rise", BassFilterQ = 1.4, HighFilterQ = 2.0,
                BassAttackMilliseconds = 3, HighAttackMilliseconds = 2, BassReleaseMilliseconds = 60, HighReleaseMilliseconds = 35,
                BassOnsetRiseWindowMilliseconds = 20, HighOnsetRiseWindowMilliseconds = 10, BassTransientSeparationMilliseconds = 160, HighTransientSeparationMilliseconds = 140,
                Sensitivity = 40, BassGainDb = 2, HighGainDb = -6,
                LowCenterHz = 90, HighCenterHz = 3500, LowThresholdDb = -32, HighThresholdDb = -30,
                MinimumSpacingMilliseconds = 160, TransientSeparationMilliseconds = 140,
                OnsetRiseDb = 5, StrongBassAboveThresholdDb = 16 }),
        new("ambient", "Ambient / sustained", "Experimental slow soft pulses during held bass (280–650 ms spacing). Pulse rate follows energy, not tempo.",
            () => new AudioSettings {
                BassTriggerMode = "level", HighTriggerMode = "level", BassFilterQ = 1.0, HighFilterQ = 1.6,
                BassAttackMilliseconds = 15, HighAttackMilliseconds = 10, BassReleaseMilliseconds = 180, HighReleaseMilliseconds = 120,
                BassOnsetRiseWindowMilliseconds = 60, HighOnsetRiseWindowMilliseconds = 40, BassTransientSeparationMilliseconds = 250, HighTransientSeparationMilliseconds = 200,
                Sensitivity = 40, HighEnabled = false,
                MinimumSpacingMilliseconds = 180, OnsetRiseDb = 6, TransientSeparationMilliseconds = 200,
                AttackMilliseconds = 15, ReleaseMilliseconds = 180, BackgroundMilliseconds = 800,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision",
                SustainEnabled = true, SustainThresholdDb = -36,
                SustainSlowIntervalMilliseconds = 650, SustainFastIntervalMilliseconds = 280 })
    });

    public static AudioSettings Create(string id) =>
        (All.FirstOrDefault(profile => profile.Id == id) ?? throw new ArgumentException("Unknown profile.", nameof(id))).Create();
}
