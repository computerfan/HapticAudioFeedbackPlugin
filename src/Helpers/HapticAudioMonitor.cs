namespace Loupedeck.HapticAudioFeedback
{
    using System;
    using NAudio.Wave;
    using NAudio.Dsp;

    /// <summary>
    /// Listens to system loopback audio, splits into low/high bands, and triggers haptic events with adaptive thresholds.
    /// Optional debug server exposes metrics at http://localhost:18888/ when enabled.
    /// </summary>
    internal sealed class HapticAudioMonitor : IDisposable
    {
        private readonly Plugin _plugin;
        private WasapiLoopbackCapture _capture;
        private readonly TimeSpan _cooldown;
        private DateTime _lastLowTriggerUtc;
        private DateTime _lastHighTriggerUtc;

        private readonly Boolean _enableDebugServer;
        private HapticMonitorDebugServer _debugServer;

        private Single _lowThresholdSmoothDb;
        private Single _highThresholdSmoothDb;

        // Per-band envelopes and adaptive noise floors
        private Single _lowEnv;
        private Single _highEnv;
        private Single _lowNoise;
        private Single _highNoise;

        // Band filters for drums/bass (low) and voice/violin (high)
        private BiQuadFilter _lowBandFilter;
        private BiQuadFilter _highBandFilter;

        // Thresholds per band
        private readonly Single _lowBandThresholdDb;
        private readonly Single _highBandThresholdDb;

        // Envelope / noise tracking tunables
        private const Single Attack = 0.8f;
        private const Single Release = 0.02f;
        private const Single NoiseFollow = 0.002f;
        private const Single MarginDb = 3.5f; // adaptive margin above noise floor
        private const Single NoiseDecayClamp = 0.995f; // max downward step per sample
        private const Single ThresholdSmooth = 0.5f;   // 0..1, higher = faster follow

        public HapticAudioMonitor(Plugin plugin, Single lowBandThresholdDb = -38f, Single highBandThresholdDb = -42f, Int32 cooldownMilliseconds = 80, Boolean enableDebugServer = false)
        {
            plugin.CheckNullArgument(nameof(plugin));
            this._plugin = plugin;
            this._lowBandThresholdDb = lowBandThresholdDb;
            this._highBandThresholdDb = highBandThresholdDb;
            this._cooldown = TimeSpan.FromMilliseconds(cooldownMilliseconds);
            this._lastLowTriggerUtc = DateTime.MinValue;
            this._lastHighTriggerUtc = DateTime.MinValue;
            this._lowThresholdSmoothDb = lowBandThresholdDb;
            this._highThresholdSmoothDb = highBandThresholdDb;
            this._enableDebugServer = enableDebugServer;
        }

        public void Start()
        {
            if (this._capture != null)
            {
                return;
            }
            try
            {
                this._capture = new WasapiLoopbackCapture();
                this.ConfigureFilters(this._capture.WaveFormat.SampleRate);
                this._capture.DataAvailable += this.OnDataAvailable;
                this._capture.StartRecording();
                if (this._enableDebugServer)
                {
                    this._debugServer = new HapticMonitorDebugServer();
                    this._debugServer.Start();
                }
                PluginLog.Info("Haptic audio monitor started.");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to start haptic audio monitor. Disabling audio-triggered haptics.");
                this.Stop();
            }
        }

        public void Stop()
        {
            if (this._capture == null)
            {
                return;
            }

            try
            {
                this._capture.DataAvailable -= this.OnDataAvailable;
                this._capture.StopRecording();
                this._capture.Dispose();
                this._lowBandFilter = null;
                this._highBandFilter = null;
                this._debugServer?.Stop();
                this._debugServer = null;
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Error while stopping haptic audio monitor.");
            }
            this._capture = null;
            PluginLog.Info("Haptic audio monitor stopped.");
        }

        public void Dispose() => this.Stop();
        
        private void OnDataAvailable(Object sender, WaveInEventArgs e)
        {
            try
            {
                if (e.BytesRecorded == 0)
                {
                    return;
                }

                var sampleCount = e.BytesRecorded / sizeof(Single);
                if (sampleCount == 0)
                {
                    return;
                }

                var samples = new Single[sampleCount];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

                Double sumLow = 0;
                Double sumHigh = 0;

                for (var i = 0; i < samples.Length; i++)
                {
                    var sample = samples[i];
                    var low = this._lowBandFilter?.Transform(sample) ?? 0f;
                    var high = this._highBandFilter?.Transform(sample) ?? 0f;
                    var lowAbs = Math.Abs(low);
                    var highAbs = Math.Abs(high);

                    this._lowEnv = lowAbs > this._lowEnv
                        ? this._lowEnv + Attack * (lowAbs - this._lowEnv)
                        : this._lowEnv + Release * (lowAbs - this._lowEnv);
                    this._highEnv = highAbs > this._highEnv
                        ? this._highEnv + Attack * (highAbs - this._highEnv)
                        : this._highEnv + Release * (highAbs - this._highEnv);

                    var lowNoiseNext = this._lowNoise + NoiseFollow * (this._lowEnv - this._lowNoise);
                    var highNoiseNext = this._highNoise + NoiseFollow * (this._highEnv - this._highNoise);

                    // Limit downward speed so noise floor doesn't dive too quickly
                    this._lowNoise = Math.Max(lowNoiseNext, this._lowNoise * NoiseDecayClamp);
                    this._highNoise = Math.Max(highNoiseNext, this._highNoise * NoiseDecayClamp);

                    sumLow += low * low;
                    sumHigh += high * high;
                }

                var lowRms = Math.Sqrt(sumLow / samples.Length);
                var highRms = Math.Sqrt(sumHigh / samples.Length);

                var lowDb = 20 * Math.Log10(Math.Max(lowRms, 1e-9));
                var highDb = 20 * Math.Log10(Math.Max(highRms, 1e-9));

                var lowEnvDb = 20 * Math.Log10(Math.Max(this._lowEnv, 1e-9f));
                var highEnvDb = 20 * Math.Log10(Math.Max(this._highEnv, 1e-9f));
                var lowNoiseDb = 20 * Math.Log10(Math.Max(this._lowNoise, 1e-9f));
                var highNoiseDb = 20 * Math.Log10(Math.Max(this._highNoise, 1e-9f));

                // Adaptive thresholds: at least configured threshold, plus margin over noise floor
                var lowThresholdRaw = Math.Max(this._lowBandThresholdDb, lowNoiseDb + MarginDb);
                var highThresholdRaw = Math.Max(this._highBandThresholdDb, highNoiseDb + MarginDb);

                // Smooth threshold changes to avoid oscillation
                this._lowThresholdSmoothDb = (Single)(this._lowThresholdSmoothDb + ThresholdSmooth * (lowThresholdRaw - this._lowThresholdSmoothDb));
                this._highThresholdSmoothDb = (Single)(this._highThresholdSmoothDb + ThresholdSmooth * (highThresholdRaw - this._highThresholdSmoothDb));

                var now = DateTime.UtcNow;
                var lowTriggered = false;
                var highTriggered = false;

                if (lowEnvDb >= this._lowThresholdSmoothDb && now - this._lastLowTriggerUtc >= this._cooldown)
                {
                    this._lastLowTriggerUtc = now;
                    this._plugin.PluginEvents.RaiseEvent("sharpAudioFeedback");
                    lowTriggered = true;
                }

                else if (highEnvDb >= this._highThresholdSmoothDb && now - this._lastHighTriggerUtc >= this._cooldown)
                {
                    this._lastHighTriggerUtc = now;
                    this._plugin.PluginEvents.RaiseEvent("subtleAudioFeedback");
                    highTriggered = true;
                }

                this._debugServer?.UpdateMetrics(new HapticMonitorSample
                {
                    Timestamp = now,
                    LowEnvDb = lowEnvDb,
                    HighEnvDb = highEnvDb,
                    LowNoiseDb = lowNoiseDb,
                    HighNoiseDb = highNoiseDb,
                    LowThresholdDb = this._lowThresholdSmoothDb,
                    HighThresholdDb = this._highThresholdSmoothDb,
                    LowTriggered = lowTriggered,
                    HighTriggered = highTriggered
                });
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Haptic audio monitor encountered an error while processing audio.");
            }
        }

        private void ConfigureFilters(Int32 sampleRate)
        {
            // Low band: kick/bass fundamentals (e.g., 60-180 Hz)
            this._lowBandFilter = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, 100f, 1.2f);
            // High band: voice/violin presence (e.g., 1¨C4 kHz center ~2 kHz)
            this._highBandFilter = BiQuadFilter.BandPassFilterConstantSkirtGain(sampleRate, 2000f, 1.6f);
        }
    }
}
