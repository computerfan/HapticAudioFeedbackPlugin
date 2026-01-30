namespace Loupedeck.HapticAudioFeedback
{
    using System;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading;

    internal sealed class HapticMonitorSample
    {
        public DateTime Timestamp { get; set; }
        public Double LowEnvDb { get; set; }
        public Double HighEnvDb { get; set; }
        public Double LowNoiseDb { get; set; }
        public Double HighNoiseDb { get; set; }
        public Double LowThresholdDb { get; set; }
        public Double HighThresholdDb { get; set; }
        public Boolean LowTriggered { get; set; }
        public Boolean HighTriggered { get; set; }
    }

    internal sealed class HapticMonitorDebugServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly Thread _thread;
        private volatile Boolean _running;
        private HapticMonitorSample _latest;
        private const Int32 Port = 18888;

        public HapticMonitorDebugServer()
        {
            this._listener = new HttpListener();
            this._listener.Prefixes.Add($"http://localhost:{Port}/");
            this._listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            this._thread = new Thread(this.Loop) { IsBackground = true, Name = "HapticMonitorDebugServer" };
        }

        public void Start()
        {
            try
            {
                this._listener.Start();
                this._running = true;
                this._thread.Start();
                PluginLog.Info($"Haptic debug server listening at http://localhost:{Port}/");
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Failed to start debug server.");
            }
        }

        public void Stop()
        {
            this._running = false;
            try
            {
                this._listener.Stop();
            }
            catch
            {
            }
        }

        public void UpdateMetrics(HapticMonitorSample sample)
        {
            this._latest = sample;
        }

        public void Dispose() => this.Stop();

        private void Loop()
        {
            while (this._running)
            {
                try
                {
                    var ctx = this._listener.GetContext();
                    this.Handle(ctx);
                }
                catch
                {
                    if (!this._running)
                    {
                        break;
                    }
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Serialize(this._latest ?? new HapticMonitorSample { Timestamp = DateTime.UtcNow });
                var bytes = Encoding.UTF8.GetBytes(payload);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentEncoding = Encoding.UTF8;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
                return;
            }

            var html = @"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<title>Haptic Debug</title>
<script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
<style>body {{ font-family: sans-serif; margin: 12px; }} canvas {{ max-width: 960px; }}</style>
</head>
<body>
<h3>Haptic Monitor (Low/High bands)</h3>
<canvas id='chart' height='260'></canvas>
<script>
const ctx = document.getElementById('chart').getContext('2d');
const labels = [];
const lowEnv = [], lowNoise = [], lowThr = [], highEnv = [], highNoise = [], highThr = [], lowTrig = [], highTrig = [];
const maxPoints = 300; // keep a rolling window like an audio track
let cursor = 0;
const chart = new Chart(ctx, {
  type: 'line',
  data: {
    labels,
    datasets: [
      { label: 'Low Env dB', data: lowEnv, borderColor: 'red', tension: 0.05, pointRadius: 0 },
      { label: 'Low Noise dB', data: lowNoise, borderColor: 'orange', tension: 0.05, borderDash: [6,3], pointRadius: 0 },
      { label: 'Low Thr dB', data: lowThr, borderColor: 'red', borderDash: [4,4], tension: 0.05, pointRadius: 0 },
      { label: 'High Env dB', data: highEnv, borderColor: 'blue', tension: 0.05, pointRadius: 0 },
      { label: 'High Noise dB', data: highNoise, borderColor: 'teal', tension: 0.05, borderDash: [6,3], pointRadius: 0 },
      { label: 'High Thr dB', data: highThr, borderColor: 'blue', borderDash: [4,4], tension: 0.05, pointRadius: 0 },
      { label: 'Low Trigger', data: lowTrig, borderColor: 'red', pointBackgroundColor: 'red', pointRadius: 4, showLine: false },
      { label: 'High Trigger', data: highTrig, borderColor: 'blue', pointBackgroundColor: 'blue', pointRadius: 4, showLine: false }
    ]
  },
  options: {
    animation: false,
    scales: {
      x: { display: false },
      y: { suggestedMin: -80, suggestedMax: 10 }
    }
  }
});
async function tick() {
  try {
    const res = await fetch('/metrics');
    const m = await res.json();
    labels.push(cursor++);
    lowEnv.push(m.LowEnvDb ?? -80);
    lowNoise.push(m.LowNoiseDb ?? -80);
    lowThr.push(m.LowThresholdDb ?? -80);
    highEnv.push(m.HighEnvDb ?? -80);
    highNoise.push(m.HighNoiseDb ?? -80);
    highThr.push(m.HighThresholdDb ?? -80);
    lowTrig.push(m.LowTriggered ? m.LowEnvDb ?? -80 : null);
    highTrig.push(m.HighTriggered ? m.HighEnvDb ?? -80 : null);
    if (labels.length > maxPoints) {
      labels.shift(); lowEnv.shift(); lowNoise.shift(); lowThr.shift(); highEnv.shift(); highNoise.shift(); highThr.shift(); lowTrig.shift(); highTrig.shift();
    }
    chart.update('none');
  } catch(e) {}
  setTimeout(tick, 30);
}
tick();
</script>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            ctx.Response.Close();
        }
    }
}
