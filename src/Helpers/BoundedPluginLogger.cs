namespace Loupedeck.HapticAudioFeedback;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Bounded producer queue and rotating files. Never performs disk I/O on callers.</summary>
internal sealed class BoundedPluginLogger : IDisposable
{
    public const int FileLimitBytes = 512 * 1024, FileCount = 3, QueueCapacity = 64, MessageLimit = 2048;
    private readonly BlockingCollection<string> _queue = new(QueueCapacity);
    private readonly object _gate = new();
    private readonly Dictionary<string, double> _recent = new();
    private readonly Func<double> _now;
    private readonly Thread _worker;
    private double _windowStart, _retryAfter;
    private int _windowCount;
    private long _suppressed, _pendingSuppressed;
    private bool _disposed;
    private string _lastError;
    public string DirectoryPath { get; }
    public long SuppressedCount { get { lock (_gate) return _suppressed; } }
    public string LastError => Volatile.Read(ref _lastError);

    public BoundedPluginLogger(string directory, Func<double> now = null)
    {
        DirectoryPath = Path.GetFullPath(directory);
        var clock = Stopwatch.StartNew();
        _now = now ?? (() => clock.Elapsed.TotalSeconds);
        _windowStart = _now();
        _worker = new Thread(Consume) { IsBackground = true, Name = "HapticLogWriter" };
        _worker.Start();
    }
    internal static string SafeText(string text, int limit = MessageLimit)
    {
        text ??= "";
        // Keep enough lookahead to redact a token crossing the truncation boundary.
        var truncated = text.Length > limit + 64;
        if (truncated) text = text[..(limit + 64)];
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ');
        text = Regex.Replace(text, "[A-Fa-f0-9]{64}", "[redacted]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        return text.Length > limit ? text[..limit] + "…" : text + (truncated ? "…" : "");
    }
    private void Suppress()
    {
        _suppressed = SaturatingCounter.Add(_suppressed, 1);
        _pendingSuppressed = SaturatingCounter.Add(_pendingSuppressed, 1);
    }
    public void Write(string level, string text, Exception error = null)
    {
        // Error handling must not throw back into audio, settings, or shutdown paths.
        try {
            lock (_gate) {
                if (_disposed) return;
                var now = _now();
                if (now - _windowStart >= 60) { _windowStart = now; _windowCount = 0; _recent.Clear(); }
                var key = level + ":" + SafeText(text, 128);
                if (_windowCount >= 30 || _recent.ContainsKey(key)) { Suppress(); return; }
                var detail = error == null ? "" : $" {error.GetType().Name}: {SafeText(error.Message, 512)} {SafeText(error.StackTrace, 768)}";
                var skipped = _pendingSuppressed == 0 ? "" : $" [suppressed {_pendingSuppressed} log messages]";
                var line = $"{DateTime.UtcNow:O} [{level}] {SafeText(text + detail)}{skipped}";
                if (!_queue.TryAdd(line)) { Suppress(); return; }
                _pendingSuppressed = 0;
                _windowCount++;
                _recent[key] = now;
            }
        } catch { /* Logging failure must never break the plugin. */ }
    }
    private string FileName(int index) => Path.Combine(DirectoryPath, index == 0 ? "feel-the-rhythm.log" : $"feel-the-rhythm.{index}.log");
    private void Consume()
    {
        try {
        foreach (var line in _queue.GetConsumingEnumerable()) {
            try {
                if (_now() < _retryAfter) { lock (_gate) Suppress(); continue; }
                Directory.CreateDirectory(DirectoryPath);
                // Only our three exact filenames are ever rotated or pruned.
                for (var i = 0; i < FileCount; i++) {
                    var file = new FileInfo(FileName(i));
                    if (file.Exists && file.Length > FileLimitBytes) File.Delete(file.FullName);
                }
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                if (File.Exists(FileName(0)) && new FileInfo(FileName(0)).Length + bytes.Length > FileLimitBytes) {
                    File.Delete(FileName(FileCount - 1));
                    for (var i = FileCount - 2; i >= 0; i--)
                        if (File.Exists(FileName(i))) File.Move(FileName(i), FileName(i + 1), true);
                }
                using (var stream = new FileStream(FileName(0), FileMode.Append, FileAccess.Write, FileShare.Read)) stream.Write(bytes);
                Volatile.Write(ref _lastError, null);
            } catch {
                Volatile.Write(ref _lastError, "Plugin log storage unavailable. Check disk space and folder permissions.");
                _retryAfter = _now() + 60;
                lock (_gate) Suppress();
            }
        }
        } finally { _queue.Dispose(); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; _queue.CompleteAdding(); }
        // A slow filesystem must not hang plugin unload. The background writer owns its queue.
        if (Thread.CurrentThread != _worker) _worker.Join(2000);
    }
}
