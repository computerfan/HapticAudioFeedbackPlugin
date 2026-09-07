using Loupedeck.HapticAudioFeedback;

var passed = 0;
void Test(string name, Action run)
{
    run();
    Console.WriteLine($"PASS {name}");
    passed++;
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
(float[] Audio, List<HapticOnset> Events, AudioOnsetDetector Detector) Analyze(
    int rate, int channels, Func<double, int, double> signal, double seconds = 2, int chunkFrames = 479, AudioSettings? options = null)
{
    var audio = new float[(int)(rate * seconds) * channels];
    for (var f = 0; f < audio.Length / channels; f++)
        for (var c = 0; c < channels; c++) audio[f * channels + c] = (float)signal((double)f / rate, c);
    var detector = new AudioOnsetDetector(rate, channels, options ?? new AudioSettings());
    var events = new List<HapticOnset>();
    for (var i = 0; i < audio.Length; i += chunkFrames * channels)
        detector.Process(audio.AsSpan(i, Math.Min(chunkFrames * channels, audio.Length - i)), events.Add);
    return (audio, events, detector);
}
double Tone(double t, double frequency, double amplitude = 0.25) => amplitude * Math.Sin(2 * Math.PI * frequency * t);

Test("rise window distinguishes sharp and gradual attacks across sample rates", () =>
{
    foreach (var rate in new[] { 44100, 48000, 96000 })
    {
        List<HapticOnset> Rising(double window) => Analyze(rate, 1,
            (t, _) => Tone(t, 100, 0.08 * Math.Pow(10, Math.Clamp((t - 1.2) / 0.1, 0, 1) * 6 / 20)),
            options: new AudioSettings { HighEnabled = false, OnsetMarginDb = 20,
                BackgroundMilliseconds = 1000, OnsetRiseWindowMilliseconds = window }).Events;
        Check(!Rising(5).Any(e => e.AudioMilliseconds >= 1200), "Short window accepted gradual rise.");
        Check(Rising(100).Any(e => e.AudioMilliseconds >= 1200 && e.AudioMilliseconds < 1400 && e.TriggerReason == "rise"),
            "Long window missed gradual rise.");
    }
});
Test("rise window defaults preserve old settings and reject invalid values", () =>
{
    var old = System.Text.Json.JsonSerializer.Deserialize<AudioSettings>("{}")!;
    Check(old.OnsetRiseWindowMilliseconds == 20, "Legacy settings changed timing.");
    foreach (var value in new[] { 0, 4, 101, double.NaN, double.PositiveInfinity })
    {
        bool rejected = false;
        try { new AudioSettings { OnsetRiseWindowMilliseconds = value }.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Invalid rise window accepted.");
    }
});
Test("trace stores the sent texture for aliases and every preset", () =>
{
    foreach (var waveform in HapticPatterns.Presets.Keys)
        foreach (var role in new[] { "bass", "strong", "high" })
        {
            var trace = new AudioTraceHistory();
            var now = DateTime.UtcNow;
            trace.Add(now, 5, -30, -40, -20, -25);
            trace.MarkSent(5, "bass", "rise", HapticPatterns.EventFor(waveform, role));
            Check(trace.Snapshot(now)[0].SentTexture == waveform, "Sent texture mapping lost.");
        }
    Check(HapticPatterns.WaveformForEvent("unknown") == null, "Unknown texture guessed.");
});
Test("silence produces no feedback", () =>
    Check(Analyze(48000, 2, (_, _) => 0).Events.Count == 0, "Silence triggered."));
Test("steady bass stops producing candidates after its initial attack", () =>
{
    var events = Analyze(48000, 2, (t, _) => Tone(t, 100), 4).Events;
    Check(events.Count(e => e.EventName != "subtleAudioFeedback") == 1 && events.All(e => e.AudioMilliseconds < 80), $"Sustained sound retriggered: {string.Join(",", events)}");
    Check(events[0].EventName == "sharpAudioFeedback", "Strong bass mapping.");
});
Test("high frequency routes to subtle feedback", () =>
{
    var events = Analyze(48000, 2, (t, _) => Tone(t, 2000), 4).Events;
    Check(events.Count == 1 && events[0].EventName == "subtleAudioFeedback", string.Join(",", events));
});
Test("quiet bass routes to damped feedback", () =>
{
    var events = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.03)).Events;
    Check(events.Count == 1 && events[0].EventName == "bassAudioFeedback", string.Join(",", events));
});
Test("identical stereo matches mono", () =>
{
    var mono = Analyze(48000, 1, (t, _) => Tone(t, 100));
    var stereo = Analyze(48000, 2, (t, _) => Tone(t, 100));
    Check(mono.Events.Select(e => (e.EventName, e.AudioMilliseconds)).SequenceEqual(stereo.Events.Select(e => (e.EventName, e.AudioMilliseconds))), "Channel count changed onset timing.");
    Check(Math.Abs(mono.Detector.Low.EnvelopeDb - stereo.Detector.Low.EnvelopeDb) < 1e-6, "Channel count changed energy.");
});
Test("opposite-phase stereo retains bass energy", () =>
{
    var normal = Analyze(48000, 2, (t, _) => Tone(t, 100));
    var opposite = Analyze(48000, 2, (t, c) => Tone(t, 100) * (c == 0 ? 1 : -1));
    Check(normal.Events.SequenceEqual(opposite.Events), "Opposite phases cancelled.");
});
Test("100 Hz filter remains centered for stereo", () =>
{
    var center = Analyze(48000, 2, (t, _) => Tone(t, 100));
    var twice = Analyze(48000, 2, (t, _) => Tone(t, 200));
    Check(center.Detector.Low.EnvelopeDb > twice.Detector.Low.EnvelopeDb + 5, "Stereo shifted band center.");
});
Test("callback chunk size does not change onset detection", () =>
{
    double Signal(double t, int c) => t % 0.6 < 0.12 ? Tone(t, c == 0 ? 100 : 2000) : 0;
    var small = Analyze(48000, 2, Signal, chunkFrames: 127);
    var large = Analyze(48000, 2, Signal, chunkFrames: 4096);
    Check(small.Events.SequenceEqual(large.Events), "Buffer boundaries changed detector output.");
});
Test("timing is stable across sample rates", () =>
{
    var timings = new List<double>();
    foreach (var rate in new[] { 44100, 48000, 96000 })
    {
        var events = Analyze(rate, 2, (t, _) => t < 0.1 ? 0 : Tone(t - 0.1, 100)).Events;
        Check(events.Count(e => e.EventName != "subtleAudioFeedback") == 1 && events.All(e => e.AudioMilliseconds < 180), $"Unexpected onset count at {rate}.");
        timings.Add(events[0].AudioMilliseconds);
    }
    Check(timings.Max() - timings.Min() < 6, "Sample rates changed onset timing by more than a window.");
});
Test("separated beats re-arm", () =>
{
    var events = Analyze(48000, 2, (t, _) => t % 0.8 < 0.12 ? Tone(t, 100) : 0, 2.4).Events;
    Check(events.Count(e => e.EventName != "subtleAudioFeedback") == 3, $"Expected three bass beats, got {events.Count} candidates.");
});
Test("transient at beginning of large callback is retained by detector", () =>
{
    var events = Analyze(48000, 2, (t, _) => t < 0.03 ? Tone(t, 100) : 0, 0.2, 9600).Events;
    Check(events.Any(e => e.EventName != "subtleAudioFeedback" && e.AudioMilliseconds < 30), "Early transient lost.");
});
Test("scheduler enforces shared cooldown and drops without backlog", () =>
{
    var scheduler = new HapticScheduler(new AudioSettings());
    var sent = new List<string>();
    scheduler.Dispatch(new[] { new HapticOnset("bass", 5, 0) }, 0, 0, sent.Add);
    scheduler.Dispatch(new[] { new HapticOnset("high", 8, 20) }, 20, 20, sent.Add);
    scheduler.Dispatch(Array.Empty<HapticOnset>(), 100, 100, sent.Add);
    scheduler.Dispatch(new[] { new HapticOnset("high", 8, 100) }, 100, 100, sent.Add);
    Check(sent.SequenceEqual(new[] { "bass", "high" }), "Cooldown or backlog failure.");
    Check(scheduler.SentCount == 2 && scheduler.DroppedCount == 1, "Incorrect counters.");
});
Test("scheduler chooses strongest fresh candidate", () =>
{
    var scheduler = new HapticScheduler(new AudioSettings());
    var sent = new List<string>();
    scheduler.Dispatch(new[] { new HapticOnset("stale", 99, 0), new HapticOnset("bass", 3, 90), new HapticOnset("high", 7, 95) }, 100, 100, sent.Add);
    Check(sent.SequenceEqual(new[] { "high" }), "Wrong arbitration.");
    Check(scheduler.DroppedCount == 2, "Arbitration drops not counted.");
});
Test("stale events are discarded", () =>
{
    var sent = new List<string>();
    new HapticScheduler(new AudioSettings()).Dispatch(new[] { new HapticOnset("old", 50, 0) }, 100, 100, sent.Add);
    Check(sent.Count == 0, "Stale event played.");
});
Test("invalid settings and partial frames are rejected", () =>
{
    var invalidSettings = false;
    try { new AudioSettings { AttackMilliseconds = double.NaN }.Validate(); }
    catch (ArgumentException) { invalidSettings = true; }
    Check(invalidSettings, "NaN accepted.");
    var invalidFrame = false;
    try { new AudioOnsetDetector(48000, 2, new AudioSettings()).Process(new float[3], _ => { }); }
    catch (ArgumentException) { invalidFrame = true; }
    Check(invalidFrame, "Partial stereo frame accepted.");
});
Test("non-finite input cannot poison filters", () =>
{
    var result = Analyze(48000, 2, (t, _) => t < 0.01 ? double.NaN : Tone(t, 100));
    Check(double.IsFinite(result.Detector.Low.EnvelopeDb), "Filter state poisoned.");
    Check(result.Events.Count > 0, "Valid audio after NaN was lost.");
});
Test("complete pipeline emits one pulse for a sustained bass tone", () =>
{
    var detector = new AudioOnsetDetector(48000, 2, new AudioSettings());
    var scheduler = new HapticScheduler(new AudioSettings());
    var sent = new List<string>();
    var buffer = new float[960];
    var candidates = new List<HapticOnset>();
    for (var block = 0; block < 400; block++)
    {
        for (var frame = 0; frame < 480; frame++)
            buffer[frame * 2] = buffer[frame * 2 + 1] = (float)Tone((block * 480.0 + frame) / 48000, 100);
        candidates.Clear();
        detector.Process(buffer, candidates.Add);
        scheduler.Dispatch(candidates, detector.AudioMilliseconds, (block + 1) * 10, sent.Add);
    }
    Check(sent.SequenceEqual(new[] { "sharpAudioFeedback" }), $"Unexpected playback: {string.Join(",", sent)}");
});
Test("a stronger high-band onset can win over bass", () =>
{
    var events = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.025) + Tone(t, 2000, 0.5), 0.1).Events;
    Check(events.Count > 0 && events[0].EventName == "subtleAudioFeedback", "Fixed bass priority remains.");
});
Test("failed backend calls still respect the shared spacing", () =>
{
    var scheduler = new HapticScheduler(new AudioSettings());
    var attempts = 0;
    void Fail(string _) { attempts++; throw new InvalidOperationException("Fake backend failure"); }
    try { scheduler.Dispatch(new[] { new HapticOnset("bass", 5, 0) }, 0, 0, Fail); }
    catch (InvalidOperationException) { }
    scheduler.Dispatch(new[] { new HapticOnset("high", 5, 10) }, 10, 10, Fail);
    Check(attempts == 1 && scheduler.SentCount == 0, "Failed sends caused a retry storm.");
});
Test("future timestamp cannot bypass age validation", () =>
{
    var sent = new List<string>();
    new HapticScheduler(new AudioSettings()).Dispatch(new[] { new HapticOnset("future", 5, 101) }, 100, 100, sent.Add);
    Check(sent.Count == 0, "Invalid future timestamp dispatched.");
});
Test("missing host assembly path falls back without crashing plugin load", () =>
{
    var warnings = 0;
    var settings = AudioSettings.Load("", _ => warnings++);
    Check(warnings == 1 && settings.MinimumSpacingMilliseconds == 80, "Empty host path was not handled.");
});
Test("settings resolve relative to host assembly file path", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "haptic-settings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var settingsPath = Path.Combine(root, "audio-settings.json");
    try
    {
        File.WriteAllText(settingsPath, "{\"MinimumSpacingMilliseconds\":125}");
        var settings = AudioSettings.Load(Path.Combine(root, "bin", "Plugin.dll"), ex => throw ex);
        Check(settings.MinimumSpacingMilliseconds == 125, "Host path failed to resolve package settings.");
        File.WriteAllText(settingsPath, "invalid json");
        var warnings = 0;
        settings = AudioSettings.Load(Path.Combine(root, "bin", "Plugin.dll"), _ => warnings++);
        Check(warnings == 1 && settings.MinimumSpacingMilliseconds == 80, "Invalid file did not fall back.");
    }
    finally { File.Delete(settingsPath); Directory.Delete(root); }
});
Test("sensitivity makes quiet bass detectable", () =>
{
    var low = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.008), options: new AudioSettings { Sensitivity = 20, HighEnabled = false });
    var high = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.008), options: new AudioSettings { Sensitivity = 90, HighEnabled = false });
    Check(low.Events.Count == 0 && high.Events.Count > 0, "Sensitivity did not change detection.");
});
Test("new bass attack is detected above a sustained note", () =>
{
    var result = Analyze(48000, 2, (t, _) => Tone(t, 100, t >= 1.2 && t < 1.3 ? 0.14 : 0.08), options: new AudioSettings { HighEnabled = false });
    Check(result.Events.Any(e => e.AudioMilliseconds >= 1200 && e.AudioMilliseconds < 1270), "Attack over sustained bass was lost.");
    Check(result.Events[0].TriggerReason == "threshold", "Initial level trigger was not classified before re-arming.");
    Check(result.Events.Any(e => e.AudioMilliseconds >= 1200 && e.AudioMilliseconds < 1270 && e.TriggerReason == "rise"), "Rapid-rise reason was lost.");
});
Test("disabled engine and disabled bass suppress all bass texture", () =>
{
    foreach (var options in new[] { new AudioSettings { Enabled = false, SustainEnabled = true },
        new AudioSettings { BassEnabled = false, HighEnabled = false, SustainEnabled = true } })
        Check(Analyze(48000, 2, (t, _) => Tone(t, 100), options: options).Events.Count == 0, "Disabled path emitted feedback.");
});
Test("custom waveform is selected for stronger bass", () =>
{
    var result = Analyze(48000, 2, (t, _) => Tone(t, 100), options: new AudioSettings { StrongBassWaveform = "subtle_collision", HighEnabled = false });
    Check(result.Events[0].EventName == "presetSubtleCollision", "Waveform override ignored.");
});
Test("sustained texture ends after bass decays", () =>
{
    var result = Analyze(48000, 2, (t, _) => t < 1 ? Tone(t, 100) : 0, options: new AudioSettings { SustainEnabled = true });
    var texture = result.Events.Where(e => e.IsSustain).ToArray();
    Check(texture.Length > 2 && texture.All(e => e.AudioMilliseconds < 1300), "Texture missing or continued into silence.");
});
Test("louder sustained bass increases pulse density", () =>
{
    var quiet = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.07), options: new AudioSettings { SustainEnabled = true });
    var loud = Analyze(48000, 2, (t, _) => Tone(t, 100, 0.5), options: new AudioSettings { SustainEnabled = true });
    Check(loud.Events.Count(e => e.IsSustain) > quiet.Events.Count(e => e.IsSustain), "Texture density did not respond to energy.");
});
Test("beat attacks take priority over sustained texture", () =>
{
    var sent = new List<string>();
    new HapticScheduler(new AudioSettings()).Dispatch(new[] {
        new HapticOnset("texture", 40, 10, true), new HapticOnset("beat", 1, 10) }, 10, 10, sent.Add);
    Check(sent.SequenceEqual(new[] { "beat" }), "Texture masked the beat.");
});
Test("runtime settings updates preserve cooldown and counters", () =>
{
    var sent = new List<string>();
    var scheduler = new HapticScheduler(new AudioSettings());
    scheduler.Dispatch(new[] { new HapticOnset("beat", 1, 0) }, 0, 0, sent.Add);
    scheduler.UpdateSettings(new AudioSettings { MinimumSpacingMilliseconds = 100 });
    scheduler.Dispatch(new[] { new HapticOnset("beat", 1, 50) }, 50, 50, sent.Add);
    Check(sent.Count == 1 && scheduler.SentCount == 1, "Settings update bypassed cooldown or reset counters.");
});
Test("saved controls round-trip and invalid changes preserve last saved version", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "haptic-controls-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(root, "settings.json");
    try
    {
        AudioSettingsStore.Save(path, new AudioSettings { Sensitivity = 73, BassWaveform = "wave" });
        try { AudioSettingsStore.Save(path, new AudioSettings { Sensitivity = 200 }); }
        catch (ArgumentException) { }
        var loaded = AudioSettingsStore.LoadOverride(path, new AudioSettings(), ex => throw ex);
        Check(loaded.Sensitivity == 73 && loaded.BassWaveform == "wave", "Saved settings were not preserved.");
    }
    finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(root)) Directory.Delete(root); }
});
Test("listening profiles are valid, distinct, and independent", () =>
{
    Check(AudioProfiles.All.Select(p => p.Id).Distinct().Count() == AudioProfiles.All.Count, "Duplicate profile ID.");
    var values = new HashSet<string>();
    foreach (var profile in AudioProfiles.All)
    {
        var settings = AudioSettings.Profile(profile.Id); settings.Validate();
        Check(values.Add(System.Text.Json.JsonSerializer.Serialize(settings)), "Two profiles have identical tuning.");
        var expected = settings.Sensitivity; settings.Sensitivity = expected + 1;
        Check(AudioSettings.Profile(profile.Id).Sensitivity == expected, "Profile instance mutated shared defaults.");
        Check(!string.IsNullOrWhiteSpace(profile.Description), "Profile has no explanation.");
    }
    var rejected = false;
    try { AudioSettings.Profile("unknown"); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Unknown profile silently changed tuning.");
});
Test("all listening profiles remain silent with silent input", () =>
{
    foreach (var profile in AudioProfiles.All)
        Check(Analyze(48000, 2, (_, _) => 0, options: profile.Create()).Events.Count == 0, profile.Id + " triggered on silence.");
});
Test("ambient texture follows held bass and stops after silence", () =>
{
    var result = Analyze(48000, 2, (t, _) => t < 3 ? Tone(t, 100, .15) : 0, 5, options: AudioSettings.Profile("ambient"));
    Check(result.Events.Count(e => e.AudioMilliseconds > 500 && e.AudioMilliseconds < 3000) >= 2, "Ambient produced no held-bass texture.");
    Check(result.Events.All(e => e.AudioMilliseconds < 4000), "Ambient continued after the bass envelope decayed.");
});
Test("SDK settings import legacy preferences once and never import the listener", () =>
{
    string saved = null!; var imports = 0;
    var store = new SdkAudioSettingsStore(() => saved, value => saved = value, ex => throw ex);
    AudioSettings Legacy() { imports++; return new AudioSettings { Sensitivity = 73, Enabled = false, EnableDebugServer = true }; }
    var first = store.Load(new AudioSettings(), Legacy);
    var second = store.Load(new AudioSettings(), Legacy);
    Check(imports == 1 && first.Sensitivity == 73 && second.Sensitivity == 73 && !second.Enabled, "Legacy settings were lost or imported again.");
    Check(!first.EnableDebugServer && !second.EnableDebugServer, "Migration reopened diagnostics.");
});
Test("SDK settings survive round-trip without re-enabling diagnostics", () =>
{
    string saved = null!;
    var store = new SdkAudioSettingsStore(() => saved, value => saved = value, ex => throw ex);
    store.Save(new AudioSettings { OnsetRiseWindowMilliseconds = 65, Sensitivity = 67, BassWaveform = "wave", EnableDebugServer = true, CaptureDeviceId = "input:CoreAudio:stable-device" });
    var loaded = store.Load(new AudioSettings(), () => throw new Exception("Unexpected migration"));
    Check(loaded.OnsetRiseWindowMilliseconds == 65 && loaded.CaptureDeviceId == "input:CoreAudio:stable-device" && loaded.Sensitivity == 67 && loaded.BassWaveform == "wave" && !loaded.EnableDebugServer, "SDK round-trip failed.");
});
Test("invalid or future SDK documents are preserved instead of overwritten", () =>
{
    foreach (var payload in new[] { "invalid", "null", "{\"Version\":2,\"Settings\":{}}", "{\"Version\":1,\"Settings\":null}" })
    {
        var errors = 0; var writes = 0;
        var store = new SdkAudioSettingsStore(() => payload, _ => writes++, _ => errors++);
        var loaded = store.Load(new AudioSettings { Sensitivity = 42 }, () => throw new Exception("Unexpected migration"));
        Check(errors == 1 && writes == 0 && loaded.Sensitivity == 42, "Invalid saved document was overwritten.");
    }
});
Test("SDK read failure does not overwrite unknown stored data", () =>
{
    var errors = 0; var writes = 0;
    var store = new SdkAudioSettingsStore(() => throw new IOException("unavailable"), _ => writes++, _ => errors++);
    store.Load(new AudioSettings(), () => throw new Exception("Unexpected migration"));
    Check(errors == 1 && writes == 0, "Read failure caused a migration write.");
});
Test("SDK save failure is surfaced and leaves the caller's settings intact", () =>
{
    var settings = new AudioSettings { Sensitivity = 71, EnableDebugServer = true };
    var store = new SdkAudioSettingsStore(() => null!, _ => throw new IOException("save failed"), _ => { });
    var failed = false;
    try { store.Save(settings); } catch (IOException) { failed = true; }
    Check(failed && settings.Sensitivity == 71 && settings.EnableDebugServer, "Save failure was hidden or mutated input.");
});
Test("custom profiles duplicate independently and survive SDK reload", () =>
{
    string? saved = null;
    var store = new CustomProfileStore(() => saved!, json => saved = json, ex => throw ex);
    var duplicate = store.Save(new() { Operation = "duplicate", Id = "music", Name = "My music", ExpectedRevision = 0 });
    var settings = store.Resolve(duplicate.SelectedId); settings.Sensitivity = 77;
    Check(store.Resolve(duplicate.SelectedId).Sensitivity == 50, "Returned settings mutated saved profile.");
    var updated = store.Save(new() { Operation = "save", Id = duplicate.SelectedId, Name = "Evening music", Settings = settings, ExpectedRevision = 1 });
    Check(updated.SelectedId == duplicate.SelectedId, "Updating changed the ID used by assigned actions.");
    var reloaded = new CustomProfileStore(() => saved!, _ => { }, ex => throw ex);
    Check(reloaded.Resolve(updated.SelectedId).Sensitivity == 77 && reloaded.Snapshot().ProfileInfo.Last().Label == "Evening music", "SDK round-trip lost custom tuning or name.");
    Check(AudioProfiles.Create("music").Sensitivity == 50, "Duplicate modified built-in tuning.");
    var second = reloaded.Save(new() { Operation = "duplicate", Id = updated.SelectedId, Name = "Another copy", ExpectedRevision = 2 });
    Check(second.SelectedId != updated.SelectedId && reloaded.Resolve(second.SelectedId).Sensitivity == 77, "Custom profile could not be duplicated.");
});
Test("custom profile save validates names, settings, and revisions without overwriting", () =>
{
    var writes = 0;
    var store = new CustomProfileStore(() => null!, _ => writes++, ex => throw ex);
    var created = store.Save(new() { Operation = "duplicate", Id = "gentle", Name = "夜间", ExpectedRevision = 0 });
    foreach (var request in new ProfileRequest[] {
        new() { Operation = "duplicate", Id = "music", Name = "夜间", ExpectedRevision = 1 },
        new() { Operation = "duplicate", Id = "music", Name = "MUSIC", ExpectedRevision = 1 },
        new() { Operation = "duplicate", Id = "music", Name = " ", ExpectedRevision = 1 },
        new() { Operation = "duplicate", Id = "music", Name = new string('x', 65), ExpectedRevision = 1 },
        new() { Operation = "duplicate", Id = "music", Name = "bad\nname", ExpectedRevision = 1 },
        new() { Operation = "duplicate", Id = "missing", Name = "New", ExpectedRevision = 1 },
        new() { Operation = "save", Id = "music", Name = "New", Settings = new(), ExpectedRevision = 1 },
        new() { Operation = "save", Name = "New", Settings = new() { Sensitivity = 101 }, ExpectedRevision = 1 },
        new() { Operation = "save", Name = "New", Settings = new(), ExpectedRevision = 0 } })
    {
        var rejected = false;
        try { store.Save(request); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { rejected = true; }
        Check(rejected && writes == 1 && store.Snapshot().ProfilesRevision == 1, "Invalid or stale save reached persistence.");
    }
});
Test("custom profile write failure preserves previous catalog", () =>
{
    var fail = false;
    var store = new CustomProfileStore(() => null!, _ => { if (fail) throw new IOException("SDK write failed"); }, _ => { });
    var created = store.Save(new() { Operation = "duplicate", Id = "music", Name = "Copy", ExpectedRevision = 0 });
    fail = true;
    try { store.Save(new() { Operation = "save", Id = created.SelectedId, Name = "Changed", Settings = new() { Sensitivity = 90 }, ExpectedRevision = 1 }); }
    catch (IOException) { }
    Check(store.Resolve(created.SelectedId).Sensitivity == 50 && store.Snapshot().ProfileInfo.Last().Label == "Copy" && store.Snapshot().ProfilesRevision == 1, "Failed SDK write published a change.");
});
Test("unreadable or newer custom profile storage is preserved and disables writes", () =>
{
    foreach (var json in new[] { "{broken", "{\"Version\":2}", "{\"Version\":1,\"Profiles\":[null]}" })
    {
        var writes = 0;
        var store = new CustomProfileStore(() => json, _ => writes++, _ => { });
        Check(store.Snapshot().ProfilesError != null && store.Snapshot().Profiles.Count == AudioProfiles.All.Count, "Corrupt document was accepted.");
        var rejected = false;
        try { store.Save(new() { Operation = "duplicate", Id = "music", Name = "Copy", ExpectedRevision = 0 }); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && writes == 0, "Unreadable saved data was overwritten.");
    }
    var unavailable = new CustomProfileStore(() => throw new IOException("SDK read failed"), _ => throw new Exception("Unexpected write"), _ => { });
    Check(unavailable.Snapshot().ProfilesError != null, "Read failure was hidden.");
});
Test("custom profiles have bounded storage and omit playback state", () =>
{
    var store = new CustomProfileStore(() => null!, _ => { }, ex => throw ex);
    var source = new AudioSettings { Enabled = false, EnableDebugServer = true };
    for (var i = 0; i < CustomProfileStore.MaximumProfiles; i++)
        store.Save(new() { Operation = "save", Name = "Custom " + i, Settings = source, ExpectedRevision = i });
    var first = store.Snapshot().ProfileInfo.First(p => p.IsCustom);
    var stored = store.Resolve(first.Id);
    Check(stored.Enabled && !stored.EnableDebugServer && !source.Enabled && source.EnableDebugServer, "Profile normalized playback state incorrectly or mutated input.");
    var rejected = false;
    try { store.Save(new() { Operation = "duplicate", Id = "music", Name = "Overflow", ExpectedRevision = CustomProfileStore.MaximumProfiles }); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Profile storage limit ignored.");
    store.Save(new() { Operation = "save", Id = first.Id, Name = first.Label, Settings = stored, ExpectedRevision = CustomProfileStore.MaximumProfiles });
    Check(store.Snapshot().ProfileInfo.Count(p => p.IsCustom) == CustomProfileStore.MaximumProfiles, "Updating at capacity changed profile count.");
});
Test("scheduler counters saturate and still account for failed sends", () =>
{
    var scheduler = new HapticScheduler(new AudioSettings());
    typeof(HapticScheduler).GetProperty("SentCount")!.SetValue(scheduler, long.MaxValue - 1);
    typeof(HapticScheduler).GetProperty("DroppedCount")!.SetValue(scheduler, long.MaxValue - 1);
    var onsets = new[] { new HapticOnset("bass", 5, 0), new HapticOnset("high", 3, 0) };
    scheduler.Dispatch(onsets, 0, 0, _ => { });
    scheduler.Dispatch(onsets, 0, 1000, _ => { });
    Check(scheduler.SentCount == long.MaxValue && scheduler.DroppedCount == long.MaxValue, "Successful sends overflowed counters.");
    var threw = false;
    try { scheduler.Dispatch(onsets, 0, 2000, _ => throw new IOException("Disconnected")); } catch (IOException) { threw = true; }
    scheduler.Dispatch(onsets, 0, 2001, _ => throw new Exception("Cooldown was bypassed"));
    Check(threw && scheduler.SentCount == long.MaxValue && scheduler.DroppedCount == long.MaxValue, "Failure or cooldown wrapped counters.");
});
Test("custom profile revision exhaustion preserves readable saved data", () =>
{
    var saved = "{\"Version\":1,\"Revision\":2147483646,\"Profiles\":[]}";
    var writes = 0;
    var store = new CustomProfileStore(() => saved, json => { saved = json; writes++; }, ex => throw ex);
    var result = store.Save(new() { Operation = "duplicate", Id = "music", Name = "Last revision", ExpectedRevision = int.MaxValue - 1 });
    var reloaded = new CustomProfileStore(() => saved, _ => writes++, ex => throw ex);
    Check(reloaded.Snapshot().ProfilesRevision == int.MaxValue && reloaded.Resolve(result.SelectedId).Sensitivity == 50, "Final revision became unreadable.");
    var rejected = false;
    try { reloaded.Save(new() { Operation = "duplicate", Id = "music", Name = "Overflow", ExpectedRevision = int.MaxValue }); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected && writes == 1 && reloaded.Snapshot().ProfilesRevision == int.MaxValue, "Exhausted revision reached storage.");
});
Test("onset telemetry preserves measured levels and bounded recent history", () =>
{
    var events = Analyze(48000, 2, (t, _) => Tone(t, 100)).Events;
    Check(events.Count > 0 && events.All(e => e.LevelDb is >= -180 and <= 0), "Missing onset level.");
    var history = new OnsetHistory();
    var now = DateTime.UtcNow;
    for (var i = 0; i < 300; i++) history.Add(now.AddMilliseconds(-1000 + i), "bass", -35, -42.5);
    var snapshot = history.Snapshot(now);
    Check(snapshot.Length == OnsetHistory.Capacity && snapshot[0].Sequence == 45 && snapshot[^1].Sequence == 300, "Telemetry capacity or order changed.");
    Check(snapshot.All(marker => marker.ThresholdDb == -42.5), "Detection threshold was not preserved.");
    history.Add(now, "preview", -20); history.Add(now, "high", double.NaN);
    Check(history.Snapshot(now).Length == OnsetHistory.Capacity, "Invalid marker accepted.");
    Check(history.Snapshot(now.AddSeconds(13)).Length == 0 && snapshot.Length == OnsetHistory.Capacity, "Telemetry expiry or snapshot isolation failed.");
});
Test("reading timestamps exclude an unfinished detector window", () =>
{
    var detector = new AudioOnsetDetector(48000, 1, new AudioSettings());
    detector.Process(new float[250], _ => { });
    Check(detector.ReadingAudioMilliseconds == 5 && detector.AudioMilliseconds > 5, "Partial window shifted the plotted reading.");
    detector.Process(new float[230], _ => { });
    Check(detector.ReadingAudioMilliseconds == 10 && detector.AudioMilliseconds == 10, "Completed window timestamp incorrect.");
});
Test("trace frames retain every detector window and the exact sent frame", () =>
{
    var baseline = Analyze(48000, 1, (t, _) => Tone(t, 100), seconds: .1);
    var detector = new AudioOnsetDetector(48000, 1, new AudioSettings());
    var readings = new List<DetectorReading>();
    var candidates = new List<HapticOnset>();
    detector.Process(baseline.Audio, candidates.Add, readings.Add);
    Check(readings.Count == 20 && readings[0].AudioMilliseconds == 5 && readings[^1].AudioMilliseconds == 100, "Lost intermediate detector frames.");
    Check(candidates.SequenceEqual(baseline.Events), "Telemetry changed detection.");
    var trace = new AudioTraceHistory();
    var now = DateTime.UtcNow;
    foreach (var reading in readings) trace.Add(now.AddMilliseconds(reading.AudioMilliseconds - 100), reading.AudioMilliseconds,
        reading.Low.EnvelopeDb, reading.High.EnvelopeDb, reading.Low.ThresholdDb, reading.High.ThresholdDb);
    var sent = candidates.First();
    Check(trace.MarkSent(sent.AudioMilliseconds, sent.Band, sent.TriggerReason), "Sent event could not find its recorded frame.");
    var point = trace.Snapshot(now).Single(point => point.SentBand != null);
    Check((sent.Band == "bass" ? point.LowEnvDb : point.HighEnvDb) == sent.LevelDb, "Dot differs from the actual line vertex.");
    Check(point.TriggerReason == sent.TriggerReason && point.TriggerReason != null, "Sent frame lost its trigger reason.");
    Check(point.Timestamp == now.AddMilliseconds(sent.AudioMilliseconds - 100), "Dot timestamp differs from its detector frame.");
});
Test("trace history bounds polling payloads, retains flags and breaks on reset", () =>
{
    var trace = new AudioTraceHistory();
    var now = DateTime.UtcNow;
    for (var i = 0; i < 3000; i++) trace.Add(now.AddMilliseconds((i - 2999) * 5), i * 5, -40, -50, -35, -45);
    Check(!trace.MarkSent(0, "bass"), "Evicted frame retained.");
    Check(trace.MarkSent(2999 * 5, "bass"), "Latest frame missing.");
    var snapshot = trace.Snapshot(now);
    Check(snapshot.Length == AudioTraceHistory.SnapshotCapacity && snapshot[^1].Sequence == 3000, "Polling payload grew past its bound.");
    Check(snapshot[^1].SentBand == "bass" && !snapshot[^1].BreakBefore, "Sent flag lost.");
    trace.Clear();
    trace.Add(now, 5, -40, -50, -35, -45);
    Check(trace.Snapshot(now).Single() is { BreakBefore: true, Sequence: 3001, SentBand: null }, "Reset joined disconnected traces or reused sequence.");
    Check(snapshot[^1].SentBand == "bass", "Snapshot changed after reset.");
    Check(trace.Snapshot(now.AddSeconds(13)).Length == 0, "Stale telemetry returned.");
});

Test("dispatch worker drops concurrent pulses without blocking callers or shutdown", () =>
{
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    using var finished = new ManualResetEventSlim();
    using var worker = new HapticDispatchWorker(_ => { });
    var calls = 0;
    try
    {
        Check(worker.TrySubmit(() => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(); finished.Set(); }), "First call rejected.");
        Check(entered.Wait(2000), "Worker did not start.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10000; i++) Check(!worker.TrySubmit(() => Interlocked.Increment(ref calls)), "Busy worker queued a pulse.");
        worker.Dispose();
        Check(watch.ElapsedMilliseconds < 1000, "Blocked backend held submission or disposal.");
        Check(!worker.TrySubmit(() => { }), "Stopped worker accepted more work.");
    }
    finally { release.Set(); finished.Wait(2000); }
    Check(calls == 1, "Backlogged pulse was played.");
});
Test("dispatch worker recovers after a backend exception", () =>
{
    using var failed = new ManualResetEventSlim();
    using var recovered = new ManualResetEventSlim();
    using var worker = new HapticDispatchWorker(_ => failed.Set());
    Check(worker.TrySubmit(() => throw new IOException("SDK failure")), "Call rejected.");
    Check(failed.Wait(2000), "Failure not reported.");
    Check(SpinWait.SpinUntil(() => worker.TrySubmit(() => recovered.Set()), 2000), "Worker did not recover.");
    Check(recovered.Wait(2000), "Recovery call missing.");
});
Test("reserved pulses count only on completion and slow calls retain cooldown", () =>
{
    var scheduler = new HapticScheduler(new AudioSettings { MinimumSpacingMilliseconds = 90 });
    var pulse = new[] { new HapticOnset("bass", 1, 0) };
    Check(scheduler.Reserve(pulse, 0, 0).HasValue && scheduler.SentCount == 0, "Reservation counted as sent.");
    scheduler.Complete(true, 500);
    Check(scheduler.SentCount == 1, "Successful completion not counted.");
    Check(!scheduler.Reserve(pulse, 0, 510).HasValue, "Slow completion allowed an immediate following pulse.");
    Check(scheduler.Reserve(pulse, 0, 600).HasValue, "Cooldown never recovered.");
    scheduler.Complete(false);
    Check(scheduler.SentCount == 1 && scheduler.DroppedCount == 2, "Rejected/failed pulses counted as sent.");
});
Console.WriteLine($"{passed} audio regression checks passed. No capture or haptic device was used.");
