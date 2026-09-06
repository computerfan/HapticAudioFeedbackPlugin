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
HapticMonitorDebugServer Create(Func<int> port = null) => new(Path.Combine(AppContext.BaseDirectory, "index.html"),
    () => new HapticMonitorSample(), () => (settings.Copy(), revision), (next, expected) =>
    {
        if (expected != revision) throw new InvalidOperationException("Settings changed elsewhere. Reload current settings.");
        settings = next.Copy(); revision++;
    }, _ => { previews++; return true; }, port);
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
Check((await Send("settings")).StatusCode == HttpStatusCode.Forbidden, "settings reads require the session token");
Check((await Send("metrics")).StatusCode == HttpStatusCode.Forbidden, "metrics reads require the session token");
Check((await Send("settings", "wrong")).StatusCode == HttpStatusCode.Forbidden, "wrong tokens are rejected");
Check((await Send("settings", token, "https://example.invalid")).StatusCode == HttpStatusCode.Forbidden, "foreign origins are rejected even with a valid token");
Check((await Send("settings", token, host: $"attacker.invalid:{new Uri(server.BaseUrl).Port}")).StatusCode != HttpStatusCode.OK, "foreign Host header cannot reach settings");
Check((await Send("settings", token)).IsSuccessStatusCode, "authenticated settings reads succeed");
var next = settings.Copy(); next.Sensitivity = 61;
Check((int)(await Send("settings", token, body: next)).StatusCode == 428 && revision == 0, "writes without a revision cannot overwrite settings");
Check((await Send("settings", token, server.BaseUrl.TrimEnd('/'), next, 0)).IsSuccessStatusCode && settings.Sensitivity == 61 && revision == 1, "valid settings save reaches the controller with its revision");
Check(!(await Send("settings", token, body: next, rev: 0)).IsSuccessStatusCode && revision == 1, "stale settings saves do not replace current settings");
next.Sensitivity = 101;
Check(!(await Send("settings", token, body: next, rev: 1)).IsSuccessStatusCode && settings.Sensitivity == 61, "invalid settings preserve current state");
Check((await Send("settings", token, body: new { Unknown = true }, rev: 1)).StatusCode == HttpStatusCode.BadRequest, "unknown settings fields are rejected");
Check((await Send("preview", token, body: "subtle_collision")).IsSuccessStatusCode && previews == 1, "authenticated preview dispatches one preset");
Check(!(await Send("preview", token, body: "invalid")).IsSuccessStatusCode && previews == 1, "invalid preview cannot dispatch");
Check((await Send("preview", body: "subtle_collision")).StatusCode == HttpStatusCode.Forbidden && previews == 1, "unauthenticated preview cannot dispatch");
Check((int)(await Send("settings", token, body: new string('x', 33000), rev: 1)).StatusCode == 413, "oversized request bodies are rejected");
server.Dispose();
using var rebound = Create(() => new Uri(server.BaseUrl).Port); rebound.Start();
Check(rebound.BaseUrl == server.BaseUrl, "dispose releases the endpoint for reuse");
Check((await Send("settings", token)).StatusCode == HttpStatusCode.Forbidden, "old session tokens fail after the same port is reused");
Console.WriteLine($"{count} browser settings integration checks passed; no audio or hardware used.");

namespace Loupedeck.HapticAudioFeedback
{
    internal static class PluginLog { public static void Info(string message) { } }
}
