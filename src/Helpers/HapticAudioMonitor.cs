namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.CoreAudioApi;

internal sealed class HapticAudioMonitor : IDisposable
{
    private readonly Plugin _plugin;
    private AudioSettings _settings;
    private readonly string _userSettingsPath, _htmlPath;
    private HapticMonitorSample _latest = new();
    private string _lastEvent;
    private DateTime? _lastSentUtc;
    private double _suppressUntilMs;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<HapticOnset> _candidates = new();
    private WasapiCapture _capture;
    private AudioOnsetDetector _detector;
    private HapticScheduler _scheduler;
    private HapticMonitorDebugServer _debugServer;
    private double _lastAudioMs;
    private string _captureMode = "starting";
    private double _backendCallMs, _maxBackendCallMs, _maxBufferMs, _maxProcessingMs, _maxLockWaitMs;
    private double _lastWarningMs = double.NegativeInfinity;

    public HapticAudioMonitor(Plugin plugin, AudioSettings settings, string userSettingsPath, string htmlPath)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        settings.Validate();
        _settings = settings.Copy();
        _userSettingsPath = userSettingsPath;
        _htmlPath = htmlPath;
    }

    public void Start()
    {
        if (_capture != null) return;
        try
        {
            _scheduler = new HapticScheduler(_settings);
            try { StartCapture(true); }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Event-driven loopback failed; trying 20 ms polling capture.");
                Stop();
                StartCapture(false);
            }
            if (_settings.EnableDebugServer)
            {
                try
                {
                    _debugServer = new HapticMonitorDebugServer(_htmlPath, GetMetrics, GetSettings, ApplySettings, Preview);
                    _debugServer.Start();
                }
                catch (Exception ex) { PluginLog.Warning(ex, "Controls unavailable; audio monitoring continues."); }
            }
            PluginLog.Info($"Audio onset monitor started: {_capture.WaveFormat}; capture {_captureMode}, requested buffer {ResponsiveLoopbackCapture.RequestedBufferMilliseconds} ms; global spacing {_settings.MinimumSpacingMilliseconds} ms.");
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Failed to start haptic audio monitor.");
            Stop();
        }
    }

    private void StartCapture(bool useEvents)
    {
        _capture = ResponsiveLoopbackCapture.Create(useEvents);
        var format = _capture.WaveFormat;
        var floatFormat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            (format is WaveFormatExtensible extended &&
             extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
        if (!floatFormat || format.BitsPerSample != 32 || format.BlockAlign != format.Channels * sizeof(float))
            throw new NotSupportedException($"Expected 32-bit float loopback audio; received {format}.");
        ResetDetector();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
        _captureMode = useEvents ? "event-driven" : "polling fallback";
    }
    private void ResetDetector()
    {
        _detector = new AudioOnsetDetector(_capture.WaveFormat.SampleRate, _capture.WaveFormat.Channels, _settings);
        _lastAudioMs = _clock.Elapsed.TotalMilliseconds;
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        var callbackEntryMs = _clock.Elapsed.TotalMilliseconds;
        lock (_gate)
        {
            if (_capture == null || !ReferenceEquals(sender, _capture) || e.BytesRecorded == 0) return;
            try
            {
                if (e.BytesRecorded % _capture.WaveFormat.BlockAlign != 0)
                    throw new InvalidOperationException("Loopback buffer contains an incomplete audio frame.");
                var now = _clock.Elapsed.TotalMilliseconds;
                var lockWaitMs = now - callbackEntryMs;
                var callbackGapMs = callbackEntryMs - _lastAudioMs;
                var bufferMs = e.BytesRecorded * 1000.0 / _capture.WaveFormat.AverageBytesPerSecond;
                _maxBufferMs = Math.Max(_maxBufferMs, bufferMs);
                _maxLockWaitMs = Math.Max(_maxLockWaitMs, lockWaitMs);
                // WASAPI may send no packets during silence. Clear old envelopes after that gap.
                if (now - _lastAudioMs > 250) ResetDetector();
                _lastAudioMs = callbackEntryMs;
                _candidates.Clear();
                var processingStartMs = _clock.Elapsed.TotalMilliseconds;
                _detector.Process(MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded)), _candidates.Add);
                var dispatchNow = _clock.Elapsed.TotalMilliseconds;
                var processingMs = dispatchNow - processingStartMs;
                _maxProcessingMs = Math.Max(_maxProcessingMs, processingMs);
                if (dispatchNow < _suppressUntilMs) _candidates.Clear();
                var sent = _scheduler.Dispatch(_candidates, _detector.AudioMilliseconds + dispatchNow - callbackEntryMs, dispatchNow,
                    Send);
                _latest = new HapticMonitorSample
                {
                    Timestamp = DateTime.UtcNow,
                    AudioReceived = true,
                    CaptureBatchMs = bufferMs,
                    CallbackGapMs = callbackGapMs,
                    ProcessingMs = processingMs,
                    LockWaitMs = lockWaitMs,
                    LowEnvDb = _detector.Low.EnvelopeDb,
                    HighEnvDb = _detector.High.EnvelopeDb,
                    LowNoiseDb = _detector.Low.BackgroundDb,
                    HighNoiseDb = _detector.High.BackgroundDb,
                    LowThresholdDb = _detector.Low.ThresholdDb,
                    HighThresholdDb = _detector.High.ThresholdDb,
                    LowTriggered = sent?.Band == "bass",
                    HighTriggered = sent?.Band == "high",
                    SentCount = _scheduler.SentCount,
                    DroppedCount = _scheduler.DroppedCount,
                    LastEvent = _lastEvent
                };
            }
            catch (Exception ex)
            {
                var now = _clock.Elapsed.TotalMilliseconds;
                if (now - _lastWarningMs >= 5000)
                {
                    PluginLog.Warning(ex, "Error processing loopback audio (warnings limited to once per 5 seconds).");
                    _lastWarningMs = now;
                }
            }
        }
    }

    private AudioSettings GetSettings() { lock (_gate) return _settings.Copy(); }

    private HapticMonitorSample GetMetrics()
    {
        lock (_gate)
        {
            var sample = _latest.Copy();
            sample.Enabled = _settings.Enabled;
            sample.Settling = _clock.Elapsed.TotalMilliseconds < _suppressUntilMs;
            sample.CaptureMode = _captureMode;
            sample.RequestedCaptureBufferMs = ResponsiveLoopbackCapture.RequestedBufferMilliseconds;
            sample.BackendCallMs = _backendCallMs;
            sample.MaxBackendCallMs = _maxBackendCallMs;
            sample.MaxCaptureBatchMs = _maxBufferMs;
            sample.MaxProcessingMs = _maxProcessingMs;
            sample.MaxLockWaitMs = _maxLockWaitMs;
            sample.LastEventAgeWithinCallbackMs = _scheduler?.LastEventAgeMilliseconds;
            sample.SentCount = _scheduler?.SentCount ?? 0;
            sample.DroppedCount = _scheduler?.DroppedCount ?? 0;
            sample.LastEvent = _lastEvent;
            sample.LastSentUtc = _lastSentUtc;
            return sample;
        }
    }

    private void ApplySettings(AudioSettings settings)
    {
        settings.Validate();
        lock (_gate)
        {
            if (_capture == null) throw new InvalidOperationException("Audio capture is not running.");
            // Prepare and persist before replacing live state. A failed save leaves the old state intact.
            var copy = settings.Copy();
            var detector = new AudioOnsetDetector(_capture.WaveFormat.SampleRate, _capture.WaveFormat.Channels, copy);
            AudioSettingsStore.Save(_userSettingsPath, copy);
            _settings = copy;
            _detector = detector;
            _scheduler.UpdateSettings(copy);
            _candidates.Clear();
            _suppressUntilMs = _clock.Elapsed.TotalMilliseconds + 400;
            PluginLog.Info("Audio controls applied and saved.");
        }
    }

    private bool Preview(string eventName)
    {
        lock (_gate)
        {
            if (_capture == null) throw new InvalidOperationException("Audio capture is not running.");
            return _scheduler.Dispatch(new[] { new HapticOnset(eventName, 0, 0) }, 0,
                _clock.Elapsed.TotalMilliseconds, Send).HasValue;
        }
    }

    private void Send(string eventName)
    {
        var started = _clock.Elapsed.TotalMilliseconds;
        try { _plugin.PluginEvents.RaiseEvent(eventName); }
        finally
        {
            _backendCallMs = _clock.Elapsed.TotalMilliseconds - started;
            _maxBackendCallMs = Math.Max(_maxBackendCallMs, _backendCallMs);
        }
        _lastEvent = eventName;
        _lastSentUtc = DateTime.UtcNow;
    }
    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            PluginLog.Error(e.Exception, "Audio capture stopped. Reload the plugin after checking the output device.");
    }

    public void Stop()
    {
        WasapiCapture capture;
        HapticMonitorDebugServer debug;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            debug = _debugServer;
            _debugServer = null;
            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
            }
        }
        // Never hold the callback lock while waiting for the capture thread to stop.
        try { capture?.StopRecording(); }
        catch (Exception ex) { PluginLog.Warning(ex, "Error stopping audio capture."); }
        finally
        {
            try { capture?.Dispose(); }
            finally { debug?.Dispose(); }
        }
    }

    public void Dispose() => Stop();
}
