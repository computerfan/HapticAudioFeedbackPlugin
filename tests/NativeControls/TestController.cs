namespace Loupedeck.HapticAudioFeedback;

// In-memory controller for exercising the production action code against real SDK controls.
public sealed class HapticAudioFeedbackPlugin : Plugin
{
    public HapticAudioFeedbackPlugin()
    {
        // The real host supplies this service before opening action editors.
        typeof(Plugin).GetProperty(nameof(Localization))!.SetValue(this,
            new PluginLocalizationEngine("HapticAudioFeedback", new TestLocalizationCallbacks()));
    }
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
internal sealed class TestLocalizationCallbacks : IPluginLocalizationEngineCallbacks
{
    private string _language = "en";
    public event EventHandler<LanguageChangedEventArgs> LanguageChanged { add { } remove { } }
    public event EventHandler<LanguageChangedEventArgs> LoupedeckLanguageChanged { add { } remove { } }
    public bool RequestPluginLanguageChange(string pluginName, string language)
    { _language = language; return true; }
    public string[] GetSupportedLanguages(string pluginName) => new[] { "en", "zh-CN" };
    public string GetPluginLanguage(string pluginName) => _language;
    public System.Globalization.CultureInfo GetPluginCultureInfo(string pluginName) => new(_language);
    public string GetLoupedeckLanguage() => "en";
    public System.Globalization.CultureInfo GetLoupedeckCultureInfo() => new("en");
    public string GetSystemLanguage() => "en";
    public bool TryGetString(string pluginName, string text, out string translation)
    { translation = text; return false; }
}
internal static class PluginLog
{
    internal static void Warning(Exception error, string message) { }
}
