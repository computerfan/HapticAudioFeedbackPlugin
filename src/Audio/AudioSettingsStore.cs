namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json;

internal static class AudioSettingsStore
{
    public static AudioSettings LoadOverride(string path, AudioSettings fallback, Action<Exception> onError)
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            var settings = JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Saved settings must be an object.");
            settings.Validate();
            return settings;
        }
        catch (Exception ex) { onError(ex); return fallback; }
    }

    public static void Save(string path, AudioSettings settings)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("Missing settings directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
