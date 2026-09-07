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
        private SdkAudioSettingsStore _settingsStore;
        private string _settingsLauncherPath;
        private CustomProfileStore _profiles;

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
            PluginLog.Start(this.GetPluginDataDirectory());
            PluginLog.Info($"Feel the Rhythm v{PluginVersion.Current} starting. Commit: {PluginVersion.Commit}.");
            this.PluginEvents.AddEvent("subtleAudioFeedback", "High-band onset", "Subtle feedback for a mid/high-frequency onset");
            this.PluginEvents.AddEvent("sharpAudioFeedback", "Strong bass onset", "Sharp feedback for a stronger bass onset");
            this.PluginEvents.AddEvent("bassAudioFeedback", "Bass onset", "Damped feedback for a bass onset");
            foreach (var preset in HapticPatterns.Presets)
                this.PluginEvents.AddEvent(preset.Value, preset.Key.Replace('_', ' '), "Audio haptic texture");
            // The host may load assembly bytes, leaving Assembly.Location empty.
            var settings = AudioSettings.Load(this.AssemblyFilePath,
                ex => PluginLog.Warning(ex, "Could not load audio settings; using defaults."));
            var userSettingsPath = System.IO.Path.Combine(this.GetPluginDataDirectory(), "audio-settings.user.json");
            this._settingsStore = new SdkAudioSettingsStore(
                () => this.TryGetPluginSetting(SdkAudioSettingsStore.SettingName, out var json) ? json : null,
                json => this.SetPluginSetting(SdkAudioSettingsStore.SettingName, json, false),
                ex => PluginLog.Warning(ex, "Could not read or migrate SDK audio settings."));
            this._profiles = new CustomProfileStore(
                () => this.TryGetPluginSetting(CustomProfileStore.SettingName, out var json) ? json : null,
                json => this.SetPluginSetting(CustomProfileStore.SettingName, json, false),
                ex => PluginLog.Warning(ex, "Could not load custom profiles; saved data was preserved."));
            var defaults = settings;
            settings = this._settingsStore.Load(defaults, () => AudioSettingsStore.LoadOverride(userSettingsPath, defaults,
                ex => PluginLog.Warning(ex, "Could not import legacy audio controls; using package settings.")));
            PluginLog.Info($"Audio preferences loaded: enabled {settings.Enabled}, sensitivity {settings.Sensitivity}, spacing {settings.MinimumSpacingMilliseconds} ms. Browser settings use a random loopback port.");
            var packageDirectory = string.IsNullOrWhiteSpace(this.AssemblyFilePath) ? "" :
                System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(this.AssemblyFilePath)) ?? "";
            var htmlPath = System.IO.Path.Combine(packageDirectory, "ui", "index.html");
            this._hapticMonitor = new HapticAudioMonitor(this, settings, this._settingsStore.Save, htmlPath, this._profiles, System.IO.Path.GetDirectoryName(this.AssemblyFilePath));
            this._hapticMonitor.Start();
            this._settingsLauncherPath = System.IO.Path.Combine(this.GetPluginDataDirectory(), "Open Haptic Settings.html");
            try { this.StartBrowserSettings(); }
            catch (Exception ex) { PluginLog.Warning(ex, "Could not start browser settings. Audio capture remains available; Open haptic settings retries."); }
        }

        private string StartBrowserSettings()
        {
            var url = this._hapticMonitor.GetOrStartSettingsUrl();
            // A stable file gives users an entry point independent of an assigned ring action.
            var html = "<!doctype html><meta charset=\"utf-8\"><title>Feel the Rhythm · Settings</title>" +
                "<meta name=\"referrer\" content=\"no-referrer\"><meta http-equiv=\"refresh\" content=\"0;url=" +
                System.Net.WebUtility.HtmlEncode(url) + "\"><p><a href=\"" + System.Net.WebUtility.HtmlEncode(url) +
                "\">Open Feel the Rhythm settings</a></p><p>If the plugin was reloaded, reopen this launcher file.</p>";
            System.IO.File.WriteAllText(this._settingsLauncherPath, html);
            PluginLog.Info("Browser settings launcher: " + this._settingsLauncherPath);
            return url;
        }
        internal void OpenSettingsWindow()
        {
            try
            {
                var url = this.StartBrowserSettings();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Could not open browser settings.");
                this.OnPluginStatusChanged(Loupedeck.PluginStatus.Warning, "Could not open browser settings: " + ex.Message);
            }
        }
        internal ProfileInfo[] AvailableProfiles => this._profiles?.Snapshot().ProfileInfo ?? AudioProfiles.All.Select(p => new ProfileInfo(p.Id, p.Label, p.Description, false)).ToArray();
        internal void ToggleAudioHaptics() => this._hapticMonitor.UpdateSettings(settings => settings.Enabled = !settings.Enabled);
        internal void SelectAudioProfile(string name) => this._hapticMonitor.UpdateSettings(settings =>
        {
            var enabled = settings.Enabled;
            var deviceId = settings.CaptureDeviceId;
            var profile = this._profiles.Resolve(name);
            foreach (var property in typeof(AudioSettings).GetProperties().Where(p => p.CanWrite))
                property.SetValue(settings, property.GetValue(profile));
            settings.Enabled = enabled;
            settings.CaptureDeviceId = deviceId;
        });
        internal bool PreviewWaveform(string waveform) => HapticPatterns.Presets.TryGetValue(waveform, out var eventName)
            ? this._hapticMonitor.Preview(eventName) : throw new ArgumentException("Unknown waveform.");

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            try
            {
                if (this._settingsLauncherPath != null)
                    System.IO.File.WriteAllText(this._settingsLauncherPath, "<!doctype html><title>Feel the Rhythm · Settings</title><p>The plugin is stopped. Start it in Logi Options+, then reopen this file.</p>");
            }
            catch (Exception ex) { PluginLog.Warning(ex, "Could not update the settings launcher."); }
            try { this._hapticMonitor?.Dispose(); }
            catch (Exception ex) { PluginLog.Warning(ex, "Could not fully stop audio capture."); }
            finally { this._hapticMonitor = null; PluginLog.Stop(); }
        }
    }
}
