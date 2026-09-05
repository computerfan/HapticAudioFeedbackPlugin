# Haptic Audio Feedback — Windows / MX Master 4

Version 0.3.1 adds event-driven audio capture and response timing diagnostics to the live music controls. It keeps the C# Actions SDK backend; native Windows haptics remain a separate feasibility probe.

## Use the live controls

Open http://localhost:18888/ after loading the plugin. Play music through the current default output device. The panel distinguishes waiting for audio, listening, quiet, paused, and disconnected. It runs locally without external chart libraries.

Start with **Music**. Changes apply and save automatically; no rebuild is needed. The detector settles for 400 ms after a change to reduce false attacks from resetting its filters. Sliders apply on release. Invalid combinations display an error and leave the previous live/saved settings intact.

| Control | What it changes |
| --- | --- |
| Sensitivity | Detects quieter or louder sounds; 0–100, with 50 as the baseline. This is not motor amplitude. |
| Bass / detail sensitivity | Adjusts each band's detection floor independently, ±12 dB. |
| Pulse spacing | Shared minimum between haptic send attempts. Increase if feedback feels busy. |
| Detect bass / detail attacks | Enables each band independently. |
| Bass, stronger bass, detail textures | Selects the preset used by each type of attack. |
| Preview buttons | Sends one selected preset through the live Logitech backend. Pause audio haptics to compare them in isolation. |
| Sustained bass texture | Optional, experimental pulse-density texture during held bass notes; off by default. |

**Music** uses sensitivity 50, 90 ms spacing and -3 dB detail gain. **Bass focus** disables detail and boosts bass detection. **Gentle** reduces sensitivity, slows playback, and uses softer waveforms. Selecting a profile resets its tuning values while preserving the current audio-haptics on/off state.

Advanced controls expose band centers, attack/release times, onset/background contrast, minimum time between detected attacks, strong-bass threshold, and maximum event age. Background tracking must be slower than attack/release; re-arm contrast must be below onset contrast. Quiet-bass texture spacing must be at least loud-bass spacing.

Controls are stored in `audio-settings.user.json` inside the SDK's plugin data directory (`GetPluginDataDirectory()`). They survive rebuilds and plugin reloads. `src/package/audio-settings.json` supplies the initial defaults; saved user controls take precedence. The control page is enabled by `EnableDebugServer` at plugin startup.

## How the algorithm works

Each audio channel has independent band-pass filters centered at 100 Hz and 2 kHz by default. Channel energies are combined after filtering, so opposite-phase stereo retains its bass. The high center is capped at 40% of the input sample rate. These are overlapping bands, not instrument recognition.

Loopback capture requests a 20 ms shared-mode buffer and uses audio-ready events. If event-driven initialization fails, it tries 20 ms polling and logs the fallback. Event-driven loopback requires Windows 10 version 1703 or later. The requested buffer is not a guarantee of end-to-end latency.

RMS is measured in approximately 5 ms windows. An onset can be detected by either a fast envelope crossing the adaptive background threshold, or a sufficiently large rise over approximately 20 ms. The second route helps detect a beat over an already-playing bass note. Hysteresis and a per-band refractory interval prevent repeated detection of one attack.

The scheduler chooses one fresh candidate per audio callback. Actual attacks take priority over sustained texture; relative strength breaks ties. All sends, including manual previews, share the playback gate. Stale, weaker, and cooldown-blocked candidates are discarded rather than queued. Settings changes preserve scheduler counters and spacing.

The supported texture palette is `subtle_collision`, `damp_collision`, `sharp_collision`, `damp_state_change`, `sharp_state_change`, and `wave`. The original three event names are preserved, and six additional preset events allow runtime selection without rewriting YAML.

### Using presets to suggest vibration

For music, start with rounded bass impacts and quiet detail taps. Reserve a stronger preset for stronger bass attacks. Leave space between pulses so that the rhythm is perceptible. Wave/transition patterns may run longer; compare them with the preview controls before using them for frequent attacks.

The optional sustained mode waits for at least 200 ms of bass energy, then emits soft or damped pulses. Energy above its threshold maps to a spacing between 260 ms (quiet) and 140 ms (loud) by default. It follows the measured envelope, never schedules a free-running repeating timer, and yields to beat attacks. It decays with the audio envelope when sound stops. It is a tactile texture, not audio waveform reproduction or a beat/BPM tracker, and can introduce a pulse rate unrelated to the music's tempo. Off is the cleaner default for rhythm.

Logitech events do not expose per-event motor frequency, amplitude, playback completion, or a stop command in the documented mapping interface. The shared spacing limits sends but cannot guarantee that a selected preset has finished. Pausing prevents new audio events; it does not cancel a preset already playing. Overall device intensity remains a Logi Options+ setting.

## Diagnostics

- Page: http://localhost:18888/
- Metrics: http://localhost:18888/metrics
- Settings snapshot: http://localhost:18888/settings
- `SentCount` counts successful calls to the event API, not confirmed physical vibrations.
- `DroppedCount` counts candidates skipped by age, priority or spacing (including backend exceptions).
- `AudioReceived` distinguishes initial placeholders from actual audio. `Timestamp` is the most recent audio sample; it stops advancing during a packet gap. `LastSentUtc` includes manual previews.
- The detector resets after a packet gap over 250 ms because WASAPI may omit packets during silence. Changing the default output device or recovering from a capture error currently requires a plugin reload.

The control server binds only localhost/127.0.0.1. Writes require its page token, same origin when an Origin header is supplied, validated JSON, and a bounded request body. Controls do not expose file paths to the browser or save audio recordings.

### Investigating delayed pulses

Expand **Response timing** to see capture mode, audio batch size, detector execution time, callback lock wait, and Logitech API call duration. Latest/maximum values reset on reload. The last event's age within its delivered batch includes processing and lock wait; it cannot account for audio buffered before the callback. The API call duration ends when `RaiseEvent` returns and excludes subsequent Logitech service, transport, or motor playback. No metric here measures physical end-to-end latency.

If the event counter increases before a vibration is felt, compare isolated preview taps with music. A useful sparse comparison is bass-only, both bass textures set to Soft tap, sustain off, and 160 ms spacing. This is an experiment, not a documented device throughput limit. The plugin drops cooldown-blocked candidates immediately; increasing spacing does not put them in a delayed queue. Logitech playback after dispatch remains unobservable through this interface.

To compare original NAudio capture batching against the new implementation on your default endpoint:

```powershell
dotnet run --project tools/CaptureTiming -c Release
```

This Windows-only diagnostic runs for six seconds, keeps the endpoint active with a silent output stream, and prints batch and callback-gap percentiles without storing audio. It does not send haptics itself; an already-running plugin can still respond to other audio. A local run on 2026-09-06 measured median batches of 60 ms for the original polling capture versus 10 ms for event-driven capture (95th percentiles 70 ms versus 10 ms). This establishes reduced batching on that endpoint, not a measured change in physical haptic latency.

## Build, test, and package

Install .NET 8 SDK or newer and Logi Options+ / Logi Plugin Service. `PluginApi.dll` is referenced from the installed host. NAudio remains at 2.2.1 to match the host-supplied runtime assemblies. The capture subclass is built into HapticAudioCapture.dll so package inspection can validate the plugin while runtime reuses the host NAudio assemblies. Do not bundle private copies of NAudio.Core.dll or NAudio.Wasapi.dll: duplicate COM interop types caused an InvalidCastException during live reload.

```powershell
dotnet run --project tests/AudioRegression -c Release
dotnet build HapticAudioFeedbackPlugin.sln -c Release -p:DeployPlugin=false
logiplugintool pack ./bin/Release/ ./bin/HapticAudioFeedbackPlugin.lplug4
logiplugintool verify ./bin/HapticAudioFeedbackPlugin.lplug4
```

Omit `-p:DeployPlugin=false` for the existing development workflow that writes the link and asks the host to reload the plugin. CI disables deployment and runs the regression suite. The assembly is named `HapticAudioFeedbackPlugin.dll` for package validation; plugin identity remains `HapticAudioFeedback`.

The host can load assembly bytes, leaving `Assembly.Location` empty. Settings and UI paths therefore use the SDK's `AssemblyFilePath`. This update does not install a new SDK/Options+ version or update mouse firmware.

## Native Windows probe

Use Windows PowerShell 5.1 (`powershell.exe`), whose WinRT projection accesses the installed API without retargeting the plugin.

```powershell
# Capability check: no UI or playback
powershell.exe -NoProfile -STA -File tools/Test-WindowsHaptics.ps1 -CheckOnly
# Interactive focus/intensity experiment
powershell.exe -NoProfile -STA -File tools/Test-WindowsHaptics.ps1
```

Click using the MX Master 4, then compare immediate playback with the delayed test after switching applications. Both execute on the probe UI thread. Each request uses one 40 ms Click effect with fallback. API result, focus and intensity are logged to the ignored `tools/windows-haptics-results.jsonl`. Record whether you actually felt each effect separately. This does not establish that the Logitech host's background audio thread can use native haptics.

## Verification

The synthetic regression suite covers stereo phase, filtering, sample rates, callback boundaries, sustained sounds, beats over a sustained baseline, sensitivity, waveform selection, optional pulse density, disabled bands, scheduling priority, cooldown, malformed inputs and saved settings. These tests never capture audio or drive hardware.

Live control verification separately checks startup, profile/slider saving, persistence across plugin reload, and preview dispatch. Tactile quality and music sensitivity still need hands-on tuning; a successful API call alone does not prove the mouse vibrated.

Official references:

- [Logitech event/waveform interface](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/)
- [Logitech haptics best practices](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-best-practices/)
- [Windows haptics and focus requirements](https://learn.microsoft.com/en-us/windows/apps/develop/input/haptics)
- [InputHapticsManager reference](https://learn.microsoft.com/en-us/uwp/api/windows.devices.haptics.inputhapticsmanager?view=winrt-28000)
- [NAudio 2.2.1 capture defaults and polling loop](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiCapture.cs)
- [Windows loopback recording and event support](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
