namespace Loupedeck.HapticAudioFeedback
{
    using System;

    using Loupedeck;

    // This class contains the plugin-level logic of the Loupedeck plugin.

    public class HapticAudioFeedbackPlugin : Plugin
    {
        // Gets a value indicating whether this is an API-only plugin.
        public override Boolean UsesApplicationApiOnly => true;

        // Gets a value indicating whether this is a Universal plugin or an Application plugin.
        public override Boolean HasNoApplication => true;

        private HapticAudioMonitor _hapticMonitor;

        // Initializes a new instance of the plugin class.
        public HapticAudioFeedbackPlugin()
        {
            // Initialize the plugin log.
            PluginLog.Init(this.Log);

            // Initialize the plugin resources.
            PluginResources.Init(this.Assembly);
        }

        // This method is called when the plugin is loaded.
        public override void Load()
        {
            this.PluginEvents.AddEvent("subtleAudioFeedback", "High-band onset", "Subtle feedback for a mid/high-frequency onset");
            this.PluginEvents.AddEvent("sharpAudioFeedback", "Strong bass onset", "Sharp feedback for a stronger bass onset");
            this.PluginEvents.AddEvent("bassAudioFeedback", "Bass onset", "Damped feedback for a bass onset");
            foreach (var preset in HapticPatterns.Presets)
                this.PluginEvents.AddEvent(preset.Value, preset.Key.Replace('_', ' '), "Audio haptic texture");
            // The host may load assembly bytes, leaving Assembly.Location empty.
            var settings = AudioSettings.Load(this.AssemblyFilePath,
                ex => PluginLog.Warning(ex, "Could not load audio settings; using defaults."));
            var userSettingsPath = System.IO.Path.Combine(this.GetPluginDataDirectory(), "audio-settings.user.json");
            settings = AudioSettingsStore.LoadOverride(userSettingsPath, settings,
                ex => PluginLog.Warning(ex, "Could not load saved audio controls; using package settings."));
            var packageDirectory = string.IsNullOrWhiteSpace(this.AssemblyFilePath) ? "" :
                System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(this.AssemblyFilePath)) ?? "";
            var htmlPath = System.IO.Path.Combine(packageDirectory, "ui", "index.html");
            this._hapticMonitor = new HapticAudioMonitor(this, settings, userSettingsPath, htmlPath);
            this._hapticMonitor.Start();
        }

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            this._hapticMonitor?.Dispose();
            this._hapticMonitor = null;
        }
    }
}
