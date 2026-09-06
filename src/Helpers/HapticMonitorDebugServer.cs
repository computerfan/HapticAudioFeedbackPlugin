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
    public string CaptureError { get; set; }
    public string ProcessingError { get; set; }
    public string CaptureWarning { get; set; }
    public string LoggingError { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long LogSuppressedCount { get; set; }
    public double NewestSampleAgeMs { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public ulong CaptureDroppedFrames { get; set; }
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
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long SentCount { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long DroppedCount { get; set; }
    public string LastEvent { get; set; }
    public DateTime? LastSentUtc { get; set; }
    public HapticMonitorSample Copy() => (HapticMonitorSample)MemberwiseClone();
}

internal sealed class HapticMonitorDebugServer : IDisposable
{
    private HttpListener _listener;
    private readonly Thread _thread;
    private readonly Func<HapticMonitorSample> _metrics;
    private readonly Func<(AudioSettings Settings, int Revision)> _settings;
    private readonly Action<AudioSettings, int?> _apply;
    private readonly Func<string, bool> _preview;
    private readonly Action _restartCapture;
    private readonly Func<object> _devices;
    private readonly string _html, _picoCss;
    private readonly CustomProfileStore _profiles;
    private readonly string _token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private readonly Func<int> _nextPort;
    public string BaseUrl { get; private set; }
    public string LaunchUrl => BaseUrl + "#token=" + _token;
    private volatile bool _running;
    public bool IsRunning => _running;

    public HapticMonitorDebugServer(string htmlPath, Func<HapticMonitorSample> metrics,
        Func<(AudioSettings Settings, int Revision)> settings, Action<AudioSettings, int?> apply, Func<string, bool> preview, Func<int> nextPort = null, CustomProfileStore profiles = null, Action restartCapture = null, Func<object> devices = null)
    {
        _html = File.ReadAllText(htmlPath);
        _picoCss = File.ReadAllText(Path.Combine(Path.GetDirectoryName(htmlPath), "vendor", "pico-2.1.1.min.css"));
        _profiles = profiles ?? new CustomProfileStore(() => null, _ => throw new InvalidOperationException("Profile storage unavailable."), _ => { });
        _metrics = metrics;
        _settings = settings;
        _apply = apply;
        _preview = preview;
        _restartCapture = restartCapture;
        _devices = devices;
        _nextPort = nextPort ?? (() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(49152, 65536));
        _thread = new Thread(Loop) { IsBackground = true, Name = "HapticMonitorControlServer" };
    }

    public void Start()
    {
        if (_running) return;
        Exception lastError = null;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var port = _nextPort();
            if (port < 49152 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var url = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            try
            {
                // Bind directly: probing and then releasing a port would leave a race.
                listener.Start();
                _listener = listener;
                BaseUrl = url;
                _running = true;
                _thread.Start();
                PluginLog.Info($"Haptic browser settings listening at {BaseUrl} (session token required).");
                return;
            }
            catch (HttpListenerException ex) { lastError = ex; listener.Close(); }
            catch { listener.Close(); throw; }
        }
        throw new IOException("Could not bind browser settings after 32 high-port attempts.", lastError);
    }
    public void Dispose()
    {
        _running = false;
        _listener?.Close();
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
                if (!_running) break;
                if (context == null) {
                    // A failed listener must stop, not spin continuously on GetContext.
                    _running = false;
                    try { _listener.Close(); } catch { }
                    PluginLog.Error(ex, "Browser listener stopped unexpectedly. Reopen settings to restart it.");
                    break;
                }
                var expected = ex is ArgumentException or InvalidOperationException or JsonException or EndOfStreamException;
                var status = ex is OperationCanceledException ? 408 : expected ? 400 : 500;
                if (!expected && status != 408) PluginLog.Warning(ex, "Browser request failed.");
                var message = expected ? ex.Message : status == 408 ? "Request timed out." : "The operation failed. Check capture status or the plugin log, then retry.";
                if (message.Length > 512) message = message[..512] + "…";
                try { Json(context, new { Error = message }, status); } catch { }
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
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        // Accept only the exact loopback authority; no wildcard hostnames or DNS rebinding.
        if (request.Url?.GetLeftPart(UriPartial.Authority) + "/" != BaseUrl ||
            request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
        { Json(context, new { Error = "Loopback requests only" }, 403); return; }
        if (request.HttpMethod != "GET" || (path != "/" && path != "/vendor/pico-2.1.1.min.css"))
        {
            var origin = request.Headers["Origin"];
            if (request.Headers["X-Haptic-Token"] != _token ||
                (origin != null && origin + "/" != BaseUrl))
            { Json(context, new { Error = "Reopen settings using the launcher or Open haptic settings action." }, 403); return; }
        }
        if (request.HttpMethod == "GET")
        {
            switch (path)
            {
                case "/devices": Json(context, _devices?.Invoke() ?? throw new InvalidOperationException("Device enumeration unavailable.")); return;
                case "/metrics": Json(context, _metrics()); return;
                case "/settings":
                    var snapshot = _settings();
                    var catalog = _profiles.Snapshot();
                    Json(context, new { snapshot.Settings, snapshot.Revision, Presets = HapticPatterns.Presets.Keys,
                        catalog.Profiles, catalog.ProfileInfo, catalog.ProfilesRevision, catalog.ProfilesError }); return;
                case "/": Write(context, _html, "text/html; charset=utf-8"); return;
                case "/vendor/pico-2.1.1.min.css": Write(context, _picoCss, "text/css; charset=utf-8"); return;
                default: Json(context, new { Error = "Not found" }, 404); return;
            }
        }
        if (request.HttpMethod != "POST") { Json(context, new { Error = "Method not allowed" }, 405); return; }
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
            if (!int.TryParse(request.Headers["If-Match"]?.Trim('"'), out var revision))
            { Json(context, new { Error = "Reload current settings before saving." }, 428); return; }
            _apply(settings, revision);
            var snapshot = _settings();
            Json(context, new { snapshot.Settings, snapshot.Revision, Saved = true });
        }
        else if (path == "/profiles")
        {
            var profile = JsonSerializer.Deserialize<ProfileRequest>(buffer, new JsonSerializerOptions
                { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new ArgumentException("Profile request required.");
            Json(context, _profiles.Save(profile));
        }
        else if (path == "/capture/restart")
        {
            if (_restartCapture == null) throw new InvalidOperationException("Capture restart unavailable.");
            _restartCapture();
            Json(context, _metrics());
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
