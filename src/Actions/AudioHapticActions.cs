namespace Loupedeck.HapticAudioFeedback;

public sealed class ConfigureAudioHaptics : PluginDynamicCommand
{
    public ConfigureAudioHaptics() : base("Open haptic settings", "Open browser settings at the current local port. Settings also have a standalone launcher file.", "Audio haptics") { }
    protected override void RunCommand(string actionParameter) => ((HapticAudioFeedbackPlugin)this.Plugin).OpenSettingsWindow();
}
public sealed class ToggleAudioHaptics : PluginDynamicCommand
{
    public ToggleAudioHaptics() : base("Toggle haptics", "Pause or resume system audio haptics.", "Audio haptics") { }
    protected override void RunCommand(string actionParameter)
    {
        try { ((HapticAudioFeedbackPlugin)this.Plugin).ToggleAudioHaptics(); }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not toggle audio haptics."); }
    }
}

public sealed class SelectAudioProfile : ActionEditorCommand
{
    public SelectAudioProfile()
    {
        this.Name = "SelectAudioProfile"; this.DisplayName = "Select haptic profile"; this.GroupName = "Audio haptics";
        this.Description = "Apply a listening profile while preserving whether audio haptics are paused.";
        this.ActionEditor.AddControlEx(new ActionEditorListbox("Profile", "Profile"));
        this.ActionEditor.ListboxItemsRequested += (_, e) =>
        {
            foreach (var profile in AudioProfiles.All) e.AddItem(profile.Id, profile.Label, profile.Description);
            if (string.IsNullOrEmpty(e.ActionEditorState.GetControlValue("Profile"))) e.SetSelectedItemName("music");
        };
    }
    protected override bool RunCommand(ActionEditorActionParameters parameters)
    {
        try { ((HapticAudioFeedbackPlugin)this.Plugin).SelectAudioProfile(parameters.GetString("Profile", "music")); return true; }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not select haptic profile."); return false; }
    }
}

public sealed class PreviewAudioHaptic : ActionEditorCommand
{
    public PreviewAudioHaptic()
    {
        this.Name = "PreviewAudioHaptic"; this.DisplayName = "Preview haptic texture"; this.GroupName = "Audio haptics";
        this.Description = "Send one preset, including while audio haptics are paused.";
        this.ActionEditor.AddControlEx(new ActionEditorListbox("Waveform", "Texture"));
        this.ActionEditor.ListboxItemsRequested += (_, e) =>
        {
            foreach (var item in HapticPatterns.WaveformNames) e.AddItem(item.Key, item.Value, "Logitech preset");
            if (string.IsNullOrEmpty(e.ActionEditorState.GetControlValue("Waveform"))) e.SetSelectedItemName("subtle_collision");
        };
    }
    protected override bool RunCommand(ActionEditorActionParameters parameters)
    {
        try { return ((HapticAudioFeedbackPlugin)this.Plugin).PreviewWaveform(parameters.GetString("Waveform", "subtle_collision")); }
        catch (Exception ex) { PluginLog.Warning(ex, "Haptic preview failed."); return false; }
    }
}
