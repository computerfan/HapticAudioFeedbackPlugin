namespace Loupedeck.HapticAudioFeedback;

internal static class HapticPatterns
{
    public static readonly IReadOnlyDictionary<string, string> Presets = new Dictionary<string, string>
    {
        ["subtle_collision"] = "presetSubtleCollision",
        ["damp_collision"] = "presetDampCollision",
        ["sharp_collision"] = "presetSharpCollision",
        ["damp_state_change"] = "presetDampStateChange",
        ["sharp_state_change"] = "presetSharpStateChange",
        ["wave"] = "presetWave"
    };

    public static readonly IReadOnlyDictionary<string, string> WaveformNames = new Dictionary<string, string>
    {
        ["subtle_collision"] = "Soft tap", ["damp_collision"] = "Rounded thump",
        ["sharp_collision"] = "Sharp impact", ["damp_state_change"] = "Soft transition",
        ["sharp_state_change"] = "Crisp tick", ["wave"] = "Wave"
    };

    public static string EventFor(string waveform, string role) => (role, waveform) switch
    {
        ("bass", "damp_collision") => "bassAudioFeedback",
        ("strong", "sharp_collision") => "sharpAudioFeedback",
        ("high", "subtle_collision") => "subtleAudioFeedback",
        _ => Presets[waveform]
    };
}
