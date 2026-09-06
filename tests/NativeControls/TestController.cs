namespace Loupedeck.HapticAudioFeedback;

// In-memory controller for exercising the production action code against real SDK controls.
public sealed class HapticAudioFeedbackPlugin : Plugin
{
    public override bool UsesApplicationApiOnly => true;
    public override bool HasNoApplication => true;
    internal AudioSettings CurrentSettings { get; private set; } = new() { Sensitivity = 73, Enabled = false };
    internal CustomProfileStore Profiles { get; } = new(() => null, _ => { }, _ => { });
    internal ProfileInfo[] AvailableProfiles => Profiles.Snapshot().ProfileInfo;
    internal int Applies { get; private set; }
    internal List<string> Previews { get; } = new();
    internal int Opens { get; private set; }
    internal void ApplyAudioSettings(AudioSettings settings) { settings.Validate(); CurrentSettings = settings.Copy(); Applies++; }
    internal void ToggleAudioHaptics() { var next = CurrentSettings.Copy(); next.Enabled = !next.Enabled; ApplyAudioSettings(next); }
    internal void SelectAudioProfile(string name) { var next = Profiles.Resolve(name); next.Enabled = CurrentSettings.Enabled; ApplyAudioSettings(next); }
    internal bool PreviewWaveform(string waveform) { if (!HapticPatterns.Presets.ContainsKey(waveform)) throw new ArgumentException("Unknown waveform"); Previews.Add(waveform); return true; }
    internal void OpenSettingsWindow() => Opens++;
}
internal static class PluginLog
{
    internal static void Warning(Exception error, string message) { }
}
