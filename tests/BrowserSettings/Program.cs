using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Loupedeck.HapticAudioFeedback;

var count = 0;
void Check(bool ok, string why) { if (!ok) throw new Exception(why); count++; Console.WriteLine("PASS " + why); }
var settings = new AudioSettings { Enabled = false };
var revision = 0;
var previews = 0;
var captureRestarts = 0;
var permissionOpens = 0;
var enumerations = 0;
Exception metricsFailure = null;
var customProfiles = new CustomProfileStore(() => null, _ => { }, ex => throw ex);
HapticMonitorDebugServer Create(Func<int> port = null) => new(Path.Combine(AppContext.BaseDirectory, "ui", "index.html"),
    () => metricsFailure != null ? throw metricsFailure : new HapticMonitorSample { SentCount = long.MaxValue, DroppedCount = 9007199254740993L, CaptureDroppedFrames = ulong.MaxValue, LogSuppressedCount = long.MaxValue, RecentAudio = new[] { new AudioTracePoint(9007199254740993L, DateTime.UtcNow, -32.5, -50, -38.25, -45, true, "bass", "threshold") }, RecentOnsets = new[] { new OnsetMarker(9007199254740993L, DateTime.UtcNow, "bass", -32.5, -38.25) } }, () => (settings.Copy(), revision), (next, expected) =>
    {
        if (expected != revision) throw new InvalidOperationException("Settings changed elsewhere. Reload current settings.");
        settings = next.Copy(); revision++;
    }, _ => { previews++; return true; }, port, customProfiles, () => captureRestarts++, () => { enumerations++; return new { Devices = new[] {
        new { Id = "output:WASAPI:stable-speaker", Name = "Speakers", Kind = "output" },
        new { Id = "input:WASAPI:stable-mic", Name = "Microphone", Kind = "input" } } }; }, () => permissionOpens++, () => new PluginLogSnapshot("/test/logs", "older entry\nrecent entry", "recent entry", Array.Empty<string>()));
if (args.Contains("--preview")) {
    using var previewServer = Create(); previewServer.Start();
    Console.WriteLine("Simulated UI preview. No audio capture or haptic events. " + previewServer.LaunchUrl);
    await Task.Delay(Timeout.Infinite); return;
}
using var first = Create(); first.Start();
var occupied = new Uri(first.BaseUrl).Port;
Check(occupied is >= 49152 and <= 65535, "port is in the high dynamic range");
var attempts = 0;
using var server = Create(() => ++attempts == 1 ? occupied : System.Security.Cryptography.RandomNumberGenerator.GetInt32(49152, 65536));
server.Start();
Check(attempts >= 2 && server.BaseUrl != first.BaseUrl, "occupied HTTP port is retried without disturbing its owner");
using (var exhausted = Create(() => occupied))
{
    try { exhausted.Start(); throw new Exception("Should have failed"); }
    catch (IOException) { Check(true, "collision retries have a finite limit"); }
}
System.Net.Sockets.TcpListener socket = null;
for (var i = 0; i < 32; i++)
{
    socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, System.Security.Cryptography.RandomNumberGenerator.GetInt32(49152, 65536));
    try { socket.Start(); break; }
    catch (System.Net.Sockets.SocketException) { socket.Stop(); socket = null; }
}
using (socket ?? throw new Exception("No TCP collision test port available"))
{
    var tcpPort = ((IPEndPoint)socket.LocalEndpoint).Port;
    var tries = 0;
    using var afterTcpCollision = Create(() => ++tries == 1 ? tcpPort : System.Security.Cryptography.RandomNumberGenerator.GetInt32(49152, 65536));
    afterTcpCollision.Start();
    Check(tries >= 2 && new Uri(afterTcpCollision.BaseUrl).Port != tcpPort, "occupied TCP port is retried");
}var token = new Uri(server.LaunchUrl).Fragment[7..];
Check(token.Length == 64 && token != new Uri(first.LaunchUrl).Fragment[7..], "separate server sessions have different 256-bit tokens");
using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl), Timeout = TimeSpan.FromSeconds(6) };
async Task<HttpResponseMessage> Send(string path, string auth = null, string origin = null, object body = null, int? rev = null, string host = null)
{
    var req = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, path);
    if (auth != null) req.Headers.Add("X-Haptic-Token", auth);
    if (origin != null) req.Headers.Add("Origin", origin);
    if (host != null) req.Headers.Host = host;
    if (rev.HasValue) req.Headers.Add("If-Match", $"\"{rev}\"");
    if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    return await client.SendAsync(req);
}
var page = await client.GetAsync("");
var html = await page.Content.ReadAsStringAsync();
Check(page.IsSuccessStatusCode && !html.Contains(token), "public HTML never exposes the session token");
Check(page.Headers.Contains("Content-Security-Policy") && page.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "page limits embedding and referrer leaks");
var licensePage = await client.GetStringAsync("licenses");
Check(html.Contains("href=\"/licenses\"") && licensePage.Contains("开源许可证") && !licensePage.Contains(token), "footer opens public bilingual license credits without a session token");
foreach (var file in new[] { "LICENSE", "FRONTEND-NOTICES.txt", "Pico-CSS-MIT.txt", "NAudio-MIT.txt", "CPAL-NOTICES.txt" })
{
    var response = await client.GetAsync("licenses/" + file);
    var diskPath = file == "LICENSE" ? Path.Combine(AppContext.BaseDirectory, file) : Path.Combine(AppContext.BaseDirectory, "licenses", file);
    Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType.MediaType == "text/plain" &&
        await response.Content.ReadAsStringAsync() == File.ReadAllText(diskPath), "bundled license is served unchanged: " + file);
}
Check((await Send("licenses/private.txt")).StatusCode == HttpStatusCode.Forbidden &&
    (await Send("licenses/private.txt", token)).StatusCode == HttpStatusCode.NotFound,
    "license route cannot expose unlisted files");
Check((await Send("licenses/LICENSE", body: new { })).StatusCode == HttpStatusCode.Forbidden,
    "public license access does not allow unauthenticated writes");
Check((await Send("settings")).StatusCode == HttpStatusCode.Forbidden, "settings reads require the session token");
Check((await Send("metrics")).StatusCode == HttpStatusCode.Forbidden, "metrics reads require the session token");
Check((await Send("settings", "wrong")).StatusCode == HttpStatusCode.Forbidden, "wrong tokens are rejected");
Check((await Send("settings", token, "https://example.invalid")).StatusCode == HttpStatusCode.Forbidden, "foreign origins are rejected even with a valid token");
Check((await Send("settings", token, host: $"attacker.invalid:{new Uri(server.BaseUrl).Port}")).StatusCode != HttpStatusCode.OK, "foreign Host header cannot reach settings");
Check((await Send("settings", token)).IsSuccessStatusCode, "authenticated settings reads succeed");
using (var metrics = JsonDocument.Parse(await (await Send("metrics", token)).Content.ReadAsStringAsync())) {
    foreach (var pair in new[] { ("SentCount", "9223372036854775807"), ("DroppedCount", "9007199254740993"), ("CaptureDroppedFrames", "18446744073709551615"), ("LogSuppressedCount", "9223372036854775807") })
        Check(metrics.RootElement.GetProperty(pair.Item1).GetString() == pair.Item2, pair.Item1 + " crosses JSON without precision loss");
    var tracePoint = metrics.RootElement.GetProperty("RecentAudio")[0];
    Check(tracePoint.GetProperty("Sequence").GetString() == "9007199254740993" && tracePoint.GetProperty("LowEnvDb").GetDouble() == -32.5 && tracePoint.GetProperty("SentBand").GetString() == "bass" && tracePoint.GetProperty("TriggerReason").GetString() == "threshold" && tracePoint.GetProperty("BreakBefore").GetBoolean(), "sent flag and recorded levels share one precision-safe trace frame");
    var onset = metrics.RootElement.GetProperty("RecentOnsets")[0];
    Check(onset.GetProperty("Sequence").GetString() == "9007199254740993", "onset sequence crosses JSON without precision loss");
    Check(onset.GetProperty("Band").GetString() == "bass" && onset.GetProperty("LevelDb").GetDouble() == -32.5 &&
        onset.GetProperty("Timestamp").GetDateTime().Kind == DateTimeKind.Utc, "onset marker includes its band, measured level and UTC timestamp");
}
metricsFailure = new IOException("Private filesystem detail " + new string('x', 10000));
var failedMetrics = await Send("metrics", token);
var failedBody = await failedMetrics.Content.ReadAsStringAsync();
Check(failedMetrics.StatusCode == HttpStatusCode.InternalServerError && failedBody.Length < 600 && !failedBody.Contains("Private filesystem"), "unexpected failures return a bounded generic 500 response");
metricsFailure = new ArgumentException(new string('x', 10000));
var invalidMetrics = await Send("metrics", token);
Check(invalidMetrics.StatusCode == HttpStatusCode.BadRequest && (await invalidMetrics.Content.ReadAsStringAsync()).Length < 600, "validation failures return bounded 400 responses");
metricsFailure = null;
Check((await Send("metrics", token)).IsSuccessStatusCode, "listener serves requests after handler failures");
var cssResponse = await client.GetAsync("vendor/pico-2.1.1.min.css");
Check(cssResponse.IsSuccessStatusCode && cssResponse.Content.Headers.ContentType.MediaType == "text/css" && (await cssResponse.Content.ReadAsStringAsync()).Contains("Pico CSS"), "bundled Pico CSS is served without exposing authenticated settings");
Check((await Send("vendor/not-allowed.css")).StatusCode == HttpStatusCode.Forbidden, "asset route does not enable arbitrary unauthenticated files");
var logoResponse = await client.GetAsync("logo.png");
Check(logoResponse.IsSuccessStatusCode && logoResponse.Content.Headers.ContentType.MediaType == "image/png" &&
    (await logoResponse.Content.ReadAsByteArrayAsync()).SequenceEqual(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "ui", "logo.png"))),
    "public branding route returns the packaged PNG unchanged");
Check((await Send("metadata/Icon256x256.png")).StatusCode == HttpStatusCode.Forbidden, "branding route does not expose arbitrary package files");
var localeResponse = await client.GetAsync("locales/zh-CN.json");
Check(localeResponse.IsSuccessStatusCode && localeResponse.Content.Headers.ContentType.MediaType == "application/json" &&
    (await localeResponse.Content.ReadAsStringAsync()).Contains("灵敏度"), "Chinese catalog is served as a public static asset");
var localeScript = await client.GetAsync("localization.js");
Check(localeScript.IsSuccessStatusCode && localeScript.Content.Headers.ContentType.MediaType == "text/javascript", "localization script is served");
Check((await Send("locales/not-allowed.json")).StatusCode == HttpStatusCode.Forbidden, "locale route does not expose arbitrary files");
var catalogResponse = await Send("settings", token);
using (var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync()))
{
    var profiles = catalog.RootElement.GetProperty("Profiles");
    var info = catalog.RootElement.GetProperty("ProfileInfo");
    Check(info.GetArrayLength() == AudioProfiles.All.Count && profiles.EnumerateObject().Count() == AudioProfiles.All.Count,
        "browser receives the complete profile catalog and descriptions");
    foreach (var entry in info.EnumerateArray()) profiles.GetProperty(entry.GetProperty("Id").GetString()).Deserialize<AudioSettings>().Validate();
}
var duplicateRequest = new ProfileRequest { Operation = "duplicate", Id = "music", Name = "My browser profile", ExpectedRevision = 0 };
Check((await Send("profiles", body: duplicateRequest)).StatusCode == HttpStatusCode.Forbidden, "custom profile writes require authentication");
var duplicatedResponse = await Send("profiles", token, body: duplicateRequest);
using var duplicated = JsonDocument.Parse(await duplicatedResponse.Content.ReadAsStringAsync());
var customId = duplicated.RootElement.GetProperty("SelectedId").GetString();
Check(duplicatedResponse.IsSuccessStatusCode && duplicated.RootElement.GetProperty("Catalog").GetProperty("ProfilesRevision").GetInt32() == 1,
    "browser can duplicate a built-in into a named custom profile");
Check(revision == 0 && !settings.Enabled, "duplicating a profile does not apply it or resume playback");
var customSettings = settings.Copy(); customSettings.Sensitivity = 68;
var customSave = new ProfileRequest { Operation = "save", Id = customId, Name = "My updated profile", Settings = customSettings, ExpectedRevision = 1 };
Check((await Send("profiles", token, body: customSave)).IsSuccessStatusCode && customProfiles.Resolve(customId).Sensitivity == 68,
    "browser can update saved custom tuning while retaining its ID");
Check(!(await Send("profiles", token, body: customSave)).IsSuccessStatusCode && customProfiles.Snapshot().ProfilesRevision == 2,
    "stale custom profile updates are rejected");
Check(!(await Send("profiles", token, "https://example.invalid", new ProfileRequest { Operation = "duplicate", Id = "music", Name = "Foreign", ExpectedRevision = 2 })).IsSuccessStatusCode,
    "foreign origins cannot save profiles");
var next = settings.Copy(); next.Sensitivity = 61;
Check((int)(await Send("settings", token, body: next)).StatusCode == 428 && revision == 0, "writes without a revision cannot overwrite settings");
Check((await Send("settings", token, server.BaseUrl.TrimEnd('/'), next, 0)).IsSuccessStatusCode && settings.Sensitivity == 61 && revision == 1, "valid settings save reaches the controller with its revision");
Check(!(await Send("settings", token, body: next, rev: 0)).IsSuccessStatusCode && revision == 1, "stale settings saves do not replace current settings");
next.Sensitivity = 101;
Check(!(await Send("settings", token, body: next, rev: 1)).IsSuccessStatusCode && settings.Sensitivity == 61, "invalid settings preserve current state");
Check((await Send("settings", token, body: new { Unknown = true }, rev: 1)).StatusCode == HttpStatusCode.BadRequest, "unknown settings fields are rejected");
using (var response = await Send("preview", token, body: "subtle_collision"))
using (var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
    Check(response.IsSuccessStatusCode && previews == 1 && result.RootElement.GetProperty("Accepted").GetBoolean()
        && !result.RootElement.TryGetProperty("Sent", out _), "preview reports acceptance without claiming completed playback");
Check(!(await Send("preview", token, body: "invalid")).IsSuccessStatusCode && previews == 1, "invalid preview cannot dispatch");
Check((await Send("preview", body: "subtle_collision")).StatusCode == HttpStatusCode.Forbidden && previews == 1, "unauthenticated preview cannot dispatch");
Check((int)(await Send("settings", token, body: new string('x', 33000), rev: 1)).StatusCode == 413, "oversized request bodies are rejected");
Check((await Send("capture/permissions", body: new { })).StatusCode == HttpStatusCode.Forbidden && permissionOpens == 0, "opening system permissions requires authentication");
Check((await Send("capture/permissions", token, "https://example.invalid", new { })).StatusCode == HttpStatusCode.Forbidden && permissionOpens == 0, "foreign origins cannot open system permissions");
Check((await Send("capture/permissions", token)).StatusCode == HttpStatusCode.NotFound && permissionOpens == 0, "GET cannot open system permissions");
Check((await Send("capture/permissions", token, body: new { })).IsSuccessStatusCode && permissionOpens == 1, "authenticated permission action reaches fixed controller");
foreach (var route in new[] { "logs", "logs/download" }) {
    Check((await Send(route)).StatusCode == HttpStatusCode.Forbidden, route + " requires authentication");
    Check((await Send(route, token, "https://example.invalid")).StatusCode == HttpStatusCode.Forbidden, route + " rejects foreign origins");
}
using (var preview = JsonDocument.Parse(await (await Send("logs", token)).Content.ReadAsStringAsync()))
    Check(preview.RootElement.GetProperty("RecentText").GetString() == "recent entry" && !preview.RootElement.TryGetProperty("Text", out _), "log preview exposes recent text without the full report");
var report = await Send("logs/download", token);
Check(report.IsSuccessStatusCode && report.Content.Headers.ContentDisposition?.DispositionType == "attachment" && (await report.Content.ReadAsStringAsync()).Contains("older entry"), "authenticated download contains retained log text with attachment headers");
Check((await Send("logs/unrelated.log", token)).StatusCode == HttpStatusCode.NotFound, "logs endpoint does not accept arbitrary file names");
Check((await Send("capture/restart", body: new { })).StatusCode == HttpStatusCode.Forbidden && captureRestarts == 0, "capture restart requires authentication");
Check((await Send("capture/restart", token, "https://example.invalid", new { })).StatusCode == HttpStatusCode.Forbidden && captureRestarts == 0, "foreign origins cannot restart capture");
Check((await Send("capture/restart", token, body: new { })).IsSuccessStatusCode && captureRestarts == 1, "authenticated capture restart reaches the controller");
Check((await Send("settings", token)).IsSuccessStatusCode, "capture restart preserves the settings endpoint session");
Check((await Send("devices")).StatusCode == HttpStatusCode.Forbidden && enumerations == 0, "device enumeration requires authentication");
Check((await Send("devices", token, "https://example.invalid")).StatusCode == HttpStatusCode.Forbidden && enumerations == 0, "foreign origins cannot enumerate audio devices");
using (var catalog = JsonDocument.Parse(await (await Send("devices", token)).Content.ReadAsStringAsync()))
    Check(catalog.RootElement.GetProperty("Devices").GetArrayLength() == 2 && enumerations == 1 && captureRestarts == 1, "authenticated enumeration returns playback and input devices without restarting capture");
var deviceSettings = settings.Copy(); deviceSettings.CaptureDeviceId = "input:WASAPI:stable-mic";
Check((await Send("settings", token, body: deviceSettings, rev: revision)).IsSuccessStatusCode && settings.CaptureDeviceId == deviceSettings.CaptureDeviceId, "explicit input choice survives settings serialization");
var savedDevice = settings.CaptureDeviceId;
foreach (var invalid in new string[] { null, "input:", "other:device", "input:bad\0id", "output:" + new string('x', 4096) }) {
    deviceSettings.CaptureDeviceId = invalid;
    Check(!(await Send("settings", token, body: deviceSettings, rev: revision)).IsSuccessStatusCode && settings.CaptureDeviceId == savedDevice, "invalid device choice preserves saved source");
}
var savedProfile = customProfiles.Save(new ProfileRequest { Operation = "save", Name = "Source-independent tuning", Settings = settings, ExpectedRevision = 2 });
Check(customProfiles.Resolve(savedProfile.SelectedId).CaptureDeviceId == "", "custom profiles exclude machine-specific audio source");
server.Dispose();
using var rebound = Create(() => new Uri(server.BaseUrl).Port); rebound.Start();
Check(rebound.BaseUrl == server.BaseUrl, "dispose releases the endpoint for reuse");
Check((await Send("settings", token)).StatusCode == HttpStatusCode.Forbidden, "old session tokens fail after the same port is reused");
using (var broken = Create()) {
    broken.Start();
    var listener = (HttpListener)typeof(HapticMonitorDebugServer).GetField("_listener", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(broken);
    listener.Close();
    Check(SpinWait.SpinUntil(() => !broken.IsRunning, 2000), "failed listener stops instead of spinning on errors");
}
Console.WriteLine($"{count} browser settings integration checks passed; no audio or hardware used.");

namespace Loupedeck.HapticAudioFeedback
{
    internal static class PluginLog { public static void Info(string message) { } public static void Warning(Exception ex, string message) { } public static void Error(Exception ex, string message) { } }
}
