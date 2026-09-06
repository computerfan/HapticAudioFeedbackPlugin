namespace Loupedeck.HapticAudioFeedback;

using System.Text.Json;

// Action names use SDK XLIFF. Runtime list items share the browser catalog.
internal static class PluginText
{
    private static readonly Lazy<Dictionary<string, string>> Chinese = new(() =>
    {
        using var stream = typeof(PluginText).Assembly.GetManifestResourceStream("HapticAudioFeedback.zh-CN.json")
            ?? throw new InvalidOperationException("Missing Chinese localization resource.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    });

    internal static string Translate(Plugin plugin, string text)
    {
        var language = plugin.Localization.CurrentLanguage;
        return string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase) &&
            Chinese.Value.TryGetValue(text, out var translation) ? translation : text;
    }
}
