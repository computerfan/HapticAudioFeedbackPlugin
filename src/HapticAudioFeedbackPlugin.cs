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
            this.PluginEvents.AddEvent(
                "subtleAudioFeedback",           // Event name (must match YAML files)
                "Play Haptic",           // Display name
                "Plays a haptic"         // Description
            );
            this.PluginEvents.AddEvent(
                "sharpAudioFeedback",           // Event name (must match YAML files)
                "Play Haptic",           // Display name
                "Plays a haptic"         // Description
            );
            this._hapticMonitor = new HapticAudioMonitor(this,  -25f, highBandThresholdDb: -44f, cooldownMilliseconds: 30, enableDebugServer: true);
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
