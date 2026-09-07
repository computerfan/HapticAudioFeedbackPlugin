namespace Loupedeck.HapticAudioFeedback;

using System.Diagnostics;

internal sealed class HapticAudioMonitor : IDisposable
{
    private readonly Plugin _plugin;
    private AudioSettings _settings;
    private readonly string _htmlPath;
    private readonly CustomProfileStore _profiles;
    private readonly Action<AudioSettings> _saveSettings;
    private readonly object _settingsGate = new(), _diagnosticsGate = new();
    private HapticMonitorSample _latest = new();
    private CaptureSignalDiagnostics _signal = new();
    private readonly OnsetHistory _onsetHistory = new();
    private readonly AudioTraceHistory _traceHistory = new();
    private int _settingsRevision;
    private string _lastEvent;
    private DateTime? _lastSentUtc;
    private double _suppressUntilMs;
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<HapticOnset> _candidates = new();
    private ISystemAudioCapture _capture;
    private readonly CaptureStartup _macStartup;
    private bool _stopped, _permissionDenied;
    private AudioOnsetDetector _detector;
    private HapticScheduler _scheduler;
    private HapticMonitorDebugServer _debugServer;
    private double _lastAudioMs;
    private string _captureMode = "starting";
    private string _captureError, _captureWarning, _processingError;
    private readonly string _binaryDirectory;
    private double _backendCallMs, _maxBackendCallMs, _maxBufferMs, _maxProcessingMs, _maxLockWaitMs;
    private double _lastWarningMs = double.NegativeInfinity;

    public HapticAudioMonitor(Plugin plugin, AudioSettings settings, Action<AudioSettings> saveSettings, string htmlPath, CustomProfileStore profiles, string binaryDirectory)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        settings.Validate();
        _settings = settings.Copy();
        _saveSettings = saveSettings;
        _htmlPath = htmlPath;
        _profiles = profiles;
        _binaryDirectory = binaryDirectory;
        _scheduler = new HapticScheduler(_settings);
        _macStartup = new CaptureStartup(_settingsGate);
    }

    public void Start()
    {
        lock (_settingsGate)
        {
            if (_stopped || _capture != null || _macStartup.IsPending) return;
            _captureError = _captureWarning = _processingError = null;
            lock (_gate) _permissionDenied = false;
            if (OperatingSystem.IsMacOS())
            {
                var deviceId = _settings.CaptureDeviceId;
                lock (_gate) _captureMode = "starting";
                _ = _macStartup.Start(token => new MacAudioCapture(_binaryDirectory, deviceId, token),
                    StartCapture, ex =>
                    {
                        lock (_settingsGate)
                        {
                            StopCapture();
                            lock (_gate)
                            {
                                _captureMode = "unavailable";
                                _permissionDenied = CapturePermissionException.IsDenied(ex);
                                _captureError = ex.Message + " Check the selected device and system audio recording permission, then retry capture.";
                            }
                            PluginLog.Warning(ex, "Could not start macOS audio capture.");
                        }
                    }, ex => PluginLog.Warning(ex, "Could not dispose a cancelled capture attempt."));
                return;
            }
            try { StartCapture(new CpalAudioCapture(_binaryDirectory, _settings.CaptureDeviceId)); }
            catch (Exception ex)
            {
                _permissionDenied = CapturePermissionException.IsDenied(ex);
                StopCapture();
                if (OperatingSystem.IsWindows() && _settings.CaptureDeviceId.Length == 0)
                {
                    _captureWarning = "CPAL unavailable; using Windows fallback. " + ex.Message;
                    PluginLog.Warning(ex, _captureWarning);
                    try { StartWindowsFallback(true); }
                    catch (Exception fallback)
                    {
                        StopCapture();
                        PluginLog.Warning(fallback, "Event-driven fallback failed; trying polling capture.");
                        try { StartWindowsFallback(false); }
                        catch (Exception last) { StopCapture(); _captureError = last.Message; _permissionDenied = CapturePermissionException.IsDenied(last); }
                    }
                }
                else _captureError = ex.Message + (OperatingSystem.IsMacOS() ? " Check the selected audio device and System Audio Recording Only permission for Feel the Rhythm Audio Capture, then retry. This error alone does not establish a permission denial." : " Select an available audio device, then retry capture.");
            }
            if (_captureError != null) { _captureMode = "unavailable"; PluginLog.Warning(_captureError); }
        }
    }
    // Keep the Windows-only type out of the cross-platform startup method's JIT dependencies.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void StartWindowsFallback(bool useEvents) => StartCapture(new WindowsAudioCapture(useEvents));
    private void StartCapture(ISystemAudioCapture capture)
    {
        _capture = capture;
        lock (_gate) { _signal = new(); _permissionDenied = false; }
        ResetDetector();
        _captureMode = capture.Mode;
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        capture.StartRecording();
        PluginLog.Info($"Audio stream opened (signal not yet verified): {_captureMode}, {capture.SampleRate} Hz, {capture.Channels} channels.");
    }
    internal object ListDevices() => new { Devices = OperatingSystem.IsMacOS()
        ? MacAudioCapture.ListDevices(_binaryDirectory) : CpalAudioCapture.ListDevices(_binaryDirectory) };
    internal void RestartCapture()
    {
        lock (_settingsGate) { _macStartup.Cancel(); StopCapture(); lock (_gate) { _latest = new(); _maxBufferMs = 0; } Start(); }
    }
    private void ResetDetector()
    {
        lock (_gate)
        {
            _traceHistory.Clear();
            _detector = new AudioOnsetDetector(_capture.SampleRate, _capture.Channels, _settings);
            _lastAudioMs = _clock.Elapsed.TotalMilliseconds;
        }
    }

    private void OnDataAvailable(object sender, AudioCaptureData e)
    {
        var callbackEntryMs = _clock.Elapsed.TotalMilliseconds;
        lock (_gate)
        {
            if (_capture == null || !ReferenceEquals(sender, _capture) || e.Samples.Length == 0) return;
            try
            {
                if (e.Samples.Length % _capture.Channels != 0)
                    throw new InvalidOperationException("Loopback buffer contains an incomplete audio frame.");
                _signal.Observe(e.Samples.Span, DateTime.UtcNow);
                var now = _clock.Elapsed.TotalMilliseconds;
                var lockWaitMs = now - callbackEntryMs;
                var callbackGapMs = callbackEntryMs - _lastAudioMs;
                var bufferMs = e.Samples.Length * 1000.0 / (_capture.SampleRate * _capture.Channels);
                _maxBufferMs = Math.Max(_maxBufferMs, bufferMs);
                _maxLockWaitMs = Math.Max(_maxLockWaitMs, lockWaitMs);
                // WASAPI may send no packets during silence. Clear old envelopes after that gap.
                if (e.Discontinuity || now - _lastAudioMs > 250) ResetDetector();
                _lastAudioMs = callbackEntryMs;
                _candidates.Clear();
                var processingStartMs = _clock.Elapsed.TotalMilliseconds;
                var audioEndMilliseconds = _detector.AudioMilliseconds + bufferMs;
                var audioTimestamp = DateTime.UtcNow.AddMilliseconds(-Math.Max(0,
                    e.NewestSampleAgeMs + _clock.Elapsed.TotalMilliseconds - callbackEntryMs));
                _detector.Process(e.Samples.Span, _candidates.Add, reading =>
                    _traceHistory.Add(audioTimestamp.AddMilliseconds(reading.AudioMilliseconds - audioEndMilliseconds),
                        reading.AudioMilliseconds, reading.Low.EnvelopeDb, reading.High.EnvelopeDb,
                        reading.Low.ThresholdDb, reading.High.ThresholdDb));
                var dispatchNow = _clock.Elapsed.TotalMilliseconds;
                var processingMs = dispatchNow - processingStartMs;
                _maxProcessingMs = Math.Max(_maxProcessingMs, processingMs);
                if (dispatchNow < _suppressUntilMs) _candidates.Clear();
                var sent = _scheduler.Dispatch(_candidates, _detector.AudioMilliseconds + e.NewestSampleAgeMs + dispatchNow - callbackEntryMs, dispatchNow,
                    Send);
                if (sent.HasValue) _processingError = null;
                // Preserve dispatched attacks between browser polls; previews and sustain pulses are excluded.
                if (sent is { IsSustain: false, LevelDb: { } level } onset)
                {
                    var onsetTimestamp = audioTimestamp.AddMilliseconds(onset.AudioMilliseconds - _detector.AudioMilliseconds);
                    _onsetHistory.Add(onsetTimestamp, onset.Band, level, level - onset.StrengthDb);
                    _traceHistory.MarkSent(onset.AudioMilliseconds, onset.Band, onset.TriggerReason);
                }
                _latest = new HapticMonitorSample
                {
                    Timestamp = audioTimestamp.AddMilliseconds(_detector.ReadingAudioMilliseconds - _detector.AudioMilliseconds),
                    AudioReceived = true,
                    CaptureBatchMs = bufferMs,
                    NewestSampleAgeMs = e.NewestSampleAgeMs,
                    CaptureDroppedFrames = e.DroppedFrames,
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
                _processingError = "Audio processing or haptic feedback failed. " + BoundedPluginLogger.SafeText(ex.Message, 512);
                _latest.AudioReceived = true; _latest.Timestamp = DateTime.UtcNow;
                var now = _clock.Elapsed.TotalMilliseconds;
                if (now - _lastWarningMs >= 5000)
                {
                    PluginLog.Warning(ex, "Error processing loopback audio (warnings limited to once per 5 seconds).");
                    _lastWarningMs = now;
                }
            }
        }
    }

    internal AudioSettings GetSettings() { lock (_gate) return _settings.Copy(); }

    internal HapticMonitorSample GetMetrics()
    {
        lock (_gate)
        {
            var sample = _latest.Copy();
            sample.CapturePackets = _signal.Packets;
            sample.CaptureSamples = _signal.Samples;
            sample.LastPacketUtc = _signal.LastPacketUtc;
            sample.LastSignalUtc = _signal.LastSignalUtc;
            sample.RawPeakDb = _signal.PeakDb;
            sample.Enabled = _settings.Enabled;
            sample.LoggingError = PluginLog.LoggingError;
            sample.LogSuppressedCount = PluginLog.SuppressedCount;
            sample.Settling = _clock.Elapsed.TotalMilliseconds < _suppressUntilMs;
            sample.CaptureMode = _captureMode;
            sample.CapturePlatform = OperatingSystem.IsMacOS() ? "macos" : "windows";
            sample.CapturePermission = _permissionDenied ? "denied" : "unknown";
            sample.CaptureSourceKind = _settings.CaptureDeviceId.StartsWith("input:", StringComparison.Ordinal) ? "input" : "output";
            sample.CaptureError = _captureError;
            sample.ProcessingError = _processingError;
            sample.CaptureWarning = _captureWarning;
            sample.RequestedCaptureBufferMs = _capture?.RequestedBufferMilliseconds ?? 20;
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
            sample.RecentOnsets = _onsetHistory.Snapshot(DateTime.UtcNow);
            sample.RecentAudio = _traceHistory.Snapshot(DateTime.UtcNow);
            return sample;
        }
    }

    internal (AudioSettings Settings, int Revision) GetSettingsSnapshot()
    {
        lock (_gate) return (_settings.Copy(), _settingsRevision);
    }
    internal void ApplySettingsIfCurrent(AudioSettings settings, int? expectedRevision)
    {
        lock (_settingsGate)
        {
            if (!expectedRevision.HasValue || GetSettingsSnapshot().Revision != expectedRevision.Value)
                throw new InvalidOperationException("Settings changed elsewhere. Reload current settings before saving this draft.");
            ApplySettingsCore(settings);
        }
    }
    internal void UpdateSettings(Action<AudioSettings> update)
    {
        lock (_settingsGate)
        {
            var settings = GetSettings();
            update(settings);
            ApplySettingsCore(settings);
        }
    }

    private void ApplySettingsCore(AudioSettings settings)
    {
        settings.Validate();
        if (_settingsRevision == int.MaxValue) throw new InvalidOperationException("Settings revision limit reached. Restart the plugin before saving more changes.");
        var nextRevision = checked(_settingsRevision + 1);
        var copy = settings.Copy();
        copy.EnableDebugServer = false;
        bool deviceChanged;
        AudioOnsetDetector detector;
        lock (_gate)
        {
            deviceChanged = copy.CaptureDeviceId != _settings.CaptureDeviceId;
            detector = _capture == null ? null : new AudioOnsetDetector(_capture.SampleRate, _capture.Channels, copy);
        }
        // SDK persistence can block. Keep it outside the audio callback lock.
        _saveSettings(copy);
        lock (_gate)
        {
            _settings = copy;
            _settingsRevision = nextRevision;
            _detector = detector;
            _scheduler.UpdateSettings(copy);
            _candidates.Clear();
            _suppressUntilMs = _clock.Elapsed.TotalMilliseconds + 400;
        }
        if (deviceChanged) RestartCapture();
        PluginLog.Info("Audio controls applied and saved through SDK settings.");
    }

    internal string GetOrStartSettingsUrl()
    {
        lock (_diagnosticsGate)
        {
            if (_debugServer?.IsRunning == true) return _debugServer.LaunchUrl;
            _debugServer?.Dispose();
            var server = new HapticMonitorDebugServer(_htmlPath, GetMetrics, GetSettingsSnapshot, ApplySettingsIfCurrent, Preview, profiles: _profiles, restartCapture: RestartCapture, devices: ListDevices, openPermissions: OpenPermissionSettings);
            try { server.Start(); lock (_gate) _debugServer = server; }
            catch { server.Dispose(); throw; }
            return server.LaunchUrl;
        }
    }

    private void OpenPermissionSettings()
    {
        var input = GetSettings().CaptureDeviceId.StartsWith("input:", StringComparison.Ordinal);
        if (OperatingSystem.IsMacOS())
        {
            var launch = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            launch.ArgumentList.Add(input
                ? "x-apple.systempreferences:com.apple.preference.security?Privacy_Microphone"
                : "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture");
            using var process = Process.Start(launch);
        }
        else if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
        }
        else throw new PlatformNotSupportedException("Open system permissions is not available on this platform.");
    }

    internal bool Preview(string eventName)
    {
        lock (_gate)
        {
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
    private void OnRecordingStopped(object sender, Exception error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _capture)) return;
            _captureError = error.Message;
            _permissionDenied = CapturePermissionException.IsDenied(error);
            _captureMode = "stopped";
            PluginLog.Error(error, "Audio capture stopped. Check the output device or permission, then retry capture.");
        }
    }
    private void StopCapture()
    {
        ISystemAudioCapture capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            _latest = new HapticMonitorSample();
            _signal = new();
            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
            }
        }
        // Never hold the callback lock while joining the consumer or releasing native capture.
        try { capture?.StopRecording(); }
        catch (Exception ex) { PluginLog.Warning(ex, "Error stopping audio capture."); }
        finally { try { capture?.Dispose(); } catch (Exception ex) { PluginLog.Warning(ex, "Error disposing audio capture."); } }
    }
    public void Stop()
    {
        lock (_settingsGate) { _stopped = true; _macStartup.Cancel(); StopCapture(); }
        lock (_diagnosticsGate)
        {
            var server = _debugServer;
            _debugServer = null;
            server?.Dispose();
        }
    }
    public void Dispose() => Stop();
}
