namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json;

// Action names use SDK XLIFF. Runtime list items share the browser catalog.
internal static class PluginText
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Catalogs = new(() =>
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(PluginText).Assembly;
        const string prefix = "HapticAudioFeedback.";
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix) || !resource.EndsWith(".json")) continue;
            var language = resource[prefix.Length..^5];
            using var stream = assembly.GetManifestResourceStream(resource);
            result[language] = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }
        return result;
    });

    internal static string Translate(Plugin plugin, string text)
    {
        var language = plugin.Localization.CurrentLanguage;
        return language != null && Catalogs.Value.TryGetValue(language, out var catalog) &&
            catalog.TryGetValue(text, out var translation) ? translation : text;
    }
}
