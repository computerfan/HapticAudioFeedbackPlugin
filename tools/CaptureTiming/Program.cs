using System.Diagnostics;
using System.Text.Json;
using Loupedeck.HapticAudioFeedback;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Compare delivery batching on the same endpoint. Audio contents are never saved.
// A silent render stream keeps loopback active even when no music is playing.
using var original = new WasapiLoopbackCapture();
using var responsive = ResponsiveLoopbackCapture.Create();
using var render = new WasapiOut(AudioClientShareMode.Shared, true, 20);
render.Init(new SilenceProvider(original.WaveFormat));
var oldStats = new CaptureStats("original: 100 ms polling", original);
var newStats = new CaptureStats("responsive: 20 ms event-driven", responsive);
original.StartRecording();
responsive.StartRecording();
render.Play();
await Task.Delay(6000);
original.StopRecording();
responsive.StopRecording();
// Dispose joins the capture threads before reading the result lists.
original.Dispose();
responsive.Dispose();
render.Stop();
Console.WriteLine(JsonSerializer.Serialize(new[] { oldStats.Result(), newStats.Result() },
    new JsonSerializerOptions { WriteIndented = true }));
if (oldStats.Count == 0 || newStats.Count == 0 || oldStats.Error != null || newStats.Error != null)
    Environment.ExitCode = 1;

sealed class CaptureStats
{
    readonly string _label;
    readonly List<double> _batches = new(), _gaps = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double? _last;
    public string? Error { get; private set; }
    public int Count => _batches.Count;
    public CaptureStats(string label, WasapiCapture capture)
    {
        _label = label;
        capture.DataAvailable += (_, e) =>
        {
            var now = _clock.Elapsed.TotalMilliseconds;
            // Exclude initialization from the steady-state comparison.
            if (now >= 1000)
            {
                _batches.Add(e.BytesRecorded * 1000.0 / capture.WaveFormat.AverageBytesPerSecond);
                if (_last.HasValue) _gaps.Add(now - _last.Value);
            }
            _last = now;
        };
        capture.RecordingStopped += (_, e) => Error = e.Exception?.ToString();
    }
    static double? Percentile(List<double> values, double fraction)
    {
        if (values.Count == 0) return null;
        var sorted = values.Order().ToArray();
        return Math.Round(sorted[(int)Math.Ceiling((sorted.Length - 1) * fraction)], 2);
    }
    public object Result() => new
    {
        Mode = _label, Callbacks = Count, Error,
        BatchMedianMs = Percentile(_batches, .5), BatchP95Ms = Percentile(_batches, .95),
        CallbackGapMedianMs = Percentile(_gaps, .5), CallbackGapP95Ms = Percentile(_gaps, .95)
    };
}
