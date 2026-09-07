namespace Loupedeck.HapticAudioFeedback;

internal static class PluginLog
{
    private static PluginLogFile _sdk;
    private static BoundedPluginLogger _writer;
    private static string _startupError;
    public static long SuppressedCount => _writer?.SuppressedCount ?? 0;
    public static string LoggingError => _startupError ?? _writer?.LastError;
    public static void Init(PluginLogFile sdk) => _sdk = sdk;
    public static void Start(string pluginDataDirectory)
    {
        try {
            _writer?.Dispose();
            _writer = new BoundedPluginLogger(Path.Combine(pluginDataDirectory, "logs"));
            _startupError = null;
            // One SDK entry locates the bounded logs; runtime messages are not duplicated there.
            try { _sdk?.Info("Feel the Rhythm diagnostic logs: " + _writer.DirectoryPath + " (three files, 512 KiB each)."); } catch { }
        } catch { _startupError = "Plugin logging could not start."; }
    }
    public static PluginLogSnapshot ReadSnapshot() => _writer?.ReadSnapshot() ?? throw new InvalidOperationException("Plugin logs are unavailable.");
    public static void Stop() { try { _writer?.Dispose(); } catch { } }
    public static void Verbose(string text) => _writer?.Write("Verbose", text);
    public static void Verbose(Exception ex, string text) => _writer?.Write("Verbose", text, ex);
    public static void Info(string text) => _writer?.Write("Info", text);
    public static void Info(Exception ex, string text) => _writer?.Write("Info", text, ex);
    public static void Warning(string text) => _writer?.Write("Warning", text);
    public static void Warning(Exception ex, string text) => _writer?.Write("Warning", text, ex);
    public static void Error(string text) => _writer?.Write("Error", text);
    public static void Error(Exception ex, string text) => _writer?.Write("Error", text, ex);
}
