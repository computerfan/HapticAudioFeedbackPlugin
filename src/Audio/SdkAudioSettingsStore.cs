#nullable enable
namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json;

/// <summary>Versioned SDK persistence, with a one-time import from the legacy JSON file.</summary>
internal sealed class SdkAudioSettingsStore(Func<string?> read, Action<string> write, Action<Exception> onError)
{
    public const string SettingName = "AudioSettingsV1";
    private sealed class Document
    {
        public int Version { get; set; }
        public AudioSettings? Settings { get; set; }
    }

    public AudioSettings Load(AudioSettings defaults, Func<AudioSettings> loadLegacy)
    {
        string? saved;
        try { saved = read(); }
        catch (Exception ex) { onError(ex); return Normalize(defaults); }
        if (saved != null)
        {
            try
            {
                var document = JsonSerializer.Deserialize<Document>(saved);
                if (document?.Version != 1 || document.Settings == null)
                    throw new InvalidDataException("Unsupported or incomplete SDK audio settings document.");
                document.Settings.Validate();
                return Normalize(document.Settings);
            }
            // Preserve unreadable or newer saved data. Never replace it during startup.
            catch (Exception ex) { onError(ex); return Normalize(defaults); }
        }
        var imported = Normalize(loadLegacy());
        try { Save(imported); }
        catch (Exception ex) { onError(ex); }
        return imported;
    }

    public void Save(AudioSettings settings)
    {
        settings.Validate();
        write(JsonSerializer.Serialize(new Document { Version = 1, Settings = Normalize(settings) }));
    }

    private static AudioSettings Normalize(AudioSettings settings)
    {
        var copy = settings.Copy();
        // Legacy debug-server preference is ignored; the browser endpoint uses a fresh session port.
        copy.EnableDebugServer = false;
        return copy;
    }
}
