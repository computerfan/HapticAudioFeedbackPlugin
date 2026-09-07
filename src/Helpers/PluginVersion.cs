namespace Loupedeck.HapticAudioFeedback;

using System.Text.RegularExpressions;
using System.Reflection;

internal static class PluginVersion
{
    // Embed the release metadata so byte-loaded SDK assemblies also report their own version.
    public static string Current { get; } = Read();
    public static string Commit { get; } = ReadCommit();
    public static string Display => Commit == "unknown" ? Current : $"{Current} ({Commit[..12]})";

    private static string ReadCommit()
    {
        var version = typeof(PluginVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var match = Regex.Match(version, @"\+([a-fA-F0-9]{40}|[a-fA-F0-9]{64})$");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "unknown";
    }

    private static string Read()
    {
        using var stream = typeof(PluginVersion).Assembly.GetManifestResourceStream("HapticPackageMetadata");
        if (stream == null) return "unknown";
        using var reader = new StreamReader(stream);
        var match = Regex.Match(reader.ReadToEnd(), @"^version:\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : "unknown";
    }
}
