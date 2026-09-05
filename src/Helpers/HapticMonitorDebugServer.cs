namespace Loupedeck.HapticAudioFeedback;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class HapticMonitorSample
{
    public DateTime Timestamp { get; set; }
    public bool AudioReceived { get; set; }
    public bool Enabled { get; set; }
    public bool Settling { get; set; }
    public string CaptureMode { get; set; }
    public int RequestedCaptureBufferMs { get; set; }
    public double CaptureBatchMs { get; set; }
    public double MaxCaptureBatchMs { get; set; }
    public double CallbackGapMs { get; set; }
    public double ProcessingMs { get; set; }
    public double MaxProcessingMs { get; set; }
    public double LockWaitMs { get; set; }
    public double MaxLockWaitMs { get; set; }
    public double BackendCallMs { get; set; }
    public double MaxBackendCallMs { get; set; }
    public double? LastEventAgeWithinCallbackMs { get; set; }
    public double LowEnvDb { get; set; } = -180;
    public double HighEnvDb { get; set; } = -180;
    public double LowNoiseDb { get; set; } = -180;
    public double HighNoiseDb { get; set; } = -180;
    public double LowThresholdDb { get; set; } = -180;
    public double HighThresholdDb { get; set; } = -180;
    public bool LowTriggered { get; set; }
    public bool HighTriggered { get; set; }
    public long SentCount { get; set; }
    public long DroppedCount { get; set; }
    public string LastEvent { get; set; }
    public DateTime? LastSentUtc { get; set; }
    public HapticMonitorSample Copy() => (HapticMonitorSample)MemberwiseClone();
}

internal sealed class HapticMonitorDebugServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Thread _thread;
    private readonly Func<HapticMonitorSample> _metrics;
    private readonly Func<AudioSettings> _settings;
    private readonly Action<AudioSettings> _apply;
    private readonly Func<string, bool> _preview;
    private readonly string _html;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private volatile bool _running;

    public HapticMonitorDebugServer(string htmlPath, Func<HapticMonitorSample> metrics,
        Func<AudioSettings> settings, Action<AudioSettings> apply, Func<string, bool> preview)
    {
        _html = File.ReadAllText(htmlPath).Replace("__CONTROL_TOKEN__", _token);
        _metrics = metrics;
        _settings = settings;
        _apply = apply;
        _preview = preview;
        _listener.Prefixes.Add("http://localhost:18888/");
        _listener.Prefixes.Add("http://127.0.0.1:18888/");
        _thread = new Thread(Loop) { IsBackground = true, Name = "HapticMonitorControlServer" };
    }

    public void Start()
    {
        _listener.Start();
        _running = true;
        _thread.Start();
        PluginLog.Info("Haptic controls listening at http://localhost:18888/");
    }

    public void Dispose()
    {
        _running = false;
        _listener.Close();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(1000);
    }

    private void Loop()
    {
        while (_running)
        {
            HttpListenerContext context = null;
            try { context = _listener.GetContext(); Handle(context); }
            catch (Exception ex)
            {
                if (context != null)
                {
                    try { Json(context, new { Error = ex.Message }, 400); }
                    catch { }
                }
                if (!_running) break;
            }
            finally { try { context?.Response.Close(); } catch { } }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? "/";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
        if (request.HttpMethod == "GET")
        {
            switch (path)
            {
                case "/metrics": Json(context, _metrics()); return;
                case "/settings":
                    Json(context, new { Settings = _settings(), Presets = HapticPatterns.Presets.Keys,
                        Profiles = new Dictionary<string, AudioSettings> {
                            ["music"] = AudioSettings.Profile("music"), ["bass"] = AudioSettings.Profile("bass"),
                            ["gentle"] = AudioSettings.Profile("gentle") } }); return;
                case "/": Write(context, _html, "text/html; charset=utf-8"); return;
                default: Json(context, new { Error = "Not found" }, 404); return;
            }
        }
        if (request.HttpMethod != "POST") { Json(context, new { Error = "Method not allowed" }, 405); return; }
        // Browser control requests must come from this local page, not an unrelated website.
        var origin = request.Headers["Origin"];
        if (request.Headers["X-Haptic-Token"] != _token ||
            (origin != null && origin != request.Url?.GetLeftPart(UriPartial.Authority)))
        { Json(context, new { Error = "Refresh the local control page and try again." }, 403); return; }
        if (!(request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? false))
        { Json(context, new { Error = "JSON required" }, 415); return; }
        if (request.ContentLength64 < 0 || request.ContentLength64 > 32768)
        { Json(context, new { Error = "Invalid request length" }, 413); return; }
        var buffer = new byte[(int)request.ContentLength64];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        request.InputStream.ReadExactlyAsync(buffer.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult();
        if (path == "/settings")
        {
            var settings = JsonSerializer.Deserialize<AudioSettings>(buffer, new JsonSerializerOptions
                { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new ArgumentException("Settings must be an object.");
            settings.Validate();
            _apply(settings);
            Json(context, new { Settings = _settings(), Saved = true });
        }
        else if (path == "/preview")
        {
            var waveform = JsonSerializer.Deserialize<string>(buffer);
            if (waveform == null || !HapticPatterns.Presets.ContainsKey(waveform))
                throw new ArgumentException("Unknown waveform.");
            Json(context, new { Sent = _preview(HapticPatterns.Presets[waveform]) });
        }
        else Json(context, new { Error = "Not found" }, 404);
    }

    private static void Json(HttpListenerContext context, object value, int status = 200)
    {
        context.Response.StatusCode = status;
        Write(context, JsonSerializer.Serialize(value), "application/json; charset=utf-8");
    }

    private static void Write(HttpListenerContext context, string text, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
