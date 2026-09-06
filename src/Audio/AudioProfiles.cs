namespace Loupedeck.HapticAudioFeedback;

internal static class AudioProfiles
{
    internal sealed record Definition(string Id, string Label, string Description, Func<AudioSettings> Create);

    public static readonly IReadOnlyList<Definition> All = Array.AsReadOnly(new Definition[]
    {
        new("music", "Music", "Balanced rhythm with rounded bass and quieter detail taps.",
            () => new AudioSettings { HighGainDb = -3, MinimumSpacingMilliseconds = 90 }),
        new("bass", "Bass focus", "Bass attacks only, with extra low-band sensitivity.",
            () => new AudioSettings { HighEnabled = false, BassGainDb = 3, MinimumSpacingMilliseconds = 100 }),
        new("gentle", "Gentle", "Fewer, softer pulses for background listening.",
            () => new AudioSettings { Sensitivity = 35, HighGainDb = -6, MinimumSpacingMilliseconds = 140,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision" }),
        new("electronic", "Electronic / dance", "Emphasizes deep bass attacks and reduces bright detail; sustained texture stays off.",
            () => new AudioSettings { Sensitivity = 55, BassGainDb = 3, HighGainDb = -6,
                LowCenterHz = 80, HighCenterHz = 3200, MinimumSpacingMilliseconds = 110,
                TransientSeparationMilliseconds = 100, StrongBassAboveThresholdDb = 10 }),
        new("rock", "Rock / live", "More separation between impacts in dense mixes, with restrained upper-band taps.",
            () => new AudioSettings { Sensitivity = 45, BassGainDb = 1, HighGainDb = -4,
                LowCenterHz = 120, HighCenterHz = 2500, MinimumSpacingMilliseconds = 120,
                TransientSeparationMilliseconds = 100, OnsetRiseDb = 4,
                ReleaseMilliseconds = 55, BackgroundMilliseconds = 350, StrongBassAboveThresholdDb = 15 }),
        new("acoustic", "Acoustic / jazz", "Softer taps and wider spacing for lighter arrangements; detail remains enabled.",
            () => new AudioSettings { Sensitivity = 55, BassGainDb = -2, HighGainDb = -2,
                LowCenterHz = 160, HighCenterHz = 1800, MinimumSpacingMilliseconds = 150,
                TransientSeparationMilliseconds = 140, OnsetRiseDb = 4, OnsetMarginDb = 8,
                AttackMilliseconds = 8, ReleaseMilliseconds = 90, BackgroundMilliseconds = 450,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision" }),
        new("cinema", "Movies / cinematic", "Sparse low-end impacts. Dialogue and soundtrack are not recognized separately.",
            () => new AudioSettings { Sensitivity = 35, BassGainDb = 4, HighEnabled = false,
                LowCenterHz = 70, LowThresholdDb = -32, MinimumSpacingMilliseconds = 220,
                TransientSeparationMilliseconds = 200, StrongBassAboveThresholdDb = 18,
                OnsetRiseDb = 5, ReleaseMilliseconds = 100, BackgroundMilliseconds = 600 }),
        new("games", "Games / action", "Separated, higher-threshold impacts. Reacts to game audio; does not identify gameplay events.",
            () => new AudioSettings { Sensitivity = 40, BassGainDb = 2, HighGainDb = -6,
                LowCenterHz = 90, HighCenterHz = 3500, LowThresholdDb = -32, HighThresholdDb = -30,
                MinimumSpacingMilliseconds = 160, TransientSeparationMilliseconds = 140,
                OnsetRiseDb = 5, StrongBassAboveThresholdDb = 16 }),
        new("ambient", "Ambient / sustained", "Experimental slow soft pulses during held bass (280–650 ms spacing). Pulse rate follows energy, not tempo.",
            () => new AudioSettings { Sensitivity = 40, HighEnabled = false,
                MinimumSpacingMilliseconds = 180, OnsetRiseDb = 6, TransientSeparationMilliseconds = 200,
                AttackMilliseconds = 15, ReleaseMilliseconds = 180, BackgroundMilliseconds = 800,
                BassWaveform = "subtle_collision", StrongBassWaveform = "damp_collision",
                SustainEnabled = true, SustainThresholdDb = -36,
                SustainSlowIntervalMilliseconds = 650, SustainFastIntervalMilliseconds = 280 })
    });

    public static AudioSettings Create(string id) =>
        (All.FirstOrDefault(profile => profile.Id == id) ?? throw new ArgumentException("Unknown profile.", nameof(id))).Create();
}
