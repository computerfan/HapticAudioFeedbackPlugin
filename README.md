# Feel the Rhythm — MX Master 4

The display name is **Feel the Rhythm**. The internal plugin ID, assembly names, action IDs, saved settings keys, and existing launcher filename remain unchanged, so existing settings and assignments continue to use the same identity.

Version 0.5.0 uses CPAL 0.18.2 for system audio capture, with the existing NAudio path as a Windows fallback. The macOS adapter uses a bundled CPAL helper app and requires macOS 14.6+. Windows capture has been exercised locally; macOS builds, permissions and hardware behavior still need validation on a Mac. Browser settings and SDK persistence remain shared.

## Open settings

The plugin writes **Open Haptic Settings.html** into its SDK user-data directory on every load. Open that file in your browser; create a shortcut to the file for convenient access. The plugin log records its full path as `Browser settings launcher:`. Reopen this launcher after a plugin reload: it always points to the current port and session. Bookmark the launcher file, rather than the temporary HTTP address. Keep it private because it contains the local session token.

Alternatively, assign **Open haptic settings** to an Actions Ring slot and activate it. This launches the browser at the current URL. You do not need an assigned action when using the standalone launcher file. Options+' plugin settings page currently exposes plugin information/language; the installed SDK does not expose a general custom form there.

| Action | Behavior |
| --- | --- |
| Open haptic settings | Opens the browser panel for the current plugin session. |
| Toggle haptics | Pauses or resumes audio-triggered feedback and saves the state. |
| Select haptic profile | Applies any of the nine listening profiles when activated; preserves paused/enabled state. |
| Preview haptic texture | Sends one selected preset, even when audio haptics are paused. |

Browser changes apply and save automatically. Basic controls include sensitivity (0–100), independent bass/detail gains, pulse spacing, band toggles and four preset selectors. Advanced controls expose frequency bands, attack/release, onset contrast, re-arm interval and event age. Optional sustained texture uses soft or damped collisions. Invalid combinations preserve the last saved settings. Settings changes allow 400 ms for the detector to settle.

**Music** uses sensitivity 50, 90 ms spacing and -3 dB detail gain. **Bass focus** disables detail and boosts bass detection. **Gentle** reduces sensitivity, slows playback and uses softer waveforms. Sensitivity changes detection, not motor amplitude; Logitech's device settings control overall intensity. A preview may be skipped if another event just occupied the shared playback slot.

Each browser page saves with the revision it loaded. If another tab or a ring action changed preferences, the old page cannot overwrite them: use **Reload saved settings** to discard that draft and load current values.

## Listening profiles

Choose a **Starting profile** in the browser, read its description, then press **Apply profile**. Choosing from the list alone does not change settings. All profiles are also available through the Options+ Select haptic profile action. Applying one replaces the tuning but preserves whether haptics are paused. Reload the plugin once after upgrading to load the expanded catalog.

| Profile | Intended starting point | Pulse spacing |
| --- | --- | --- |
| Music | Balanced bass and detail attacks | 90 ms |
| Bass focus | Bass-only rhythm | 100 ms |
| Gentle | Softer, less frequent background feedback | 140 ms |
| Electronic / dance | Deep bass emphasis and reduced bright detail | 110 ms |
| Rock / live | Separated impacts in dense mixes | 120 ms |
| Acoustic / jazz | Soft taps for lighter arrangements | 150 ms |
| Movies / cinematic | Sparse, higher-threshold low-end impacts | 220 ms |
| Games / action | Higher thresholds and separated bass/detail impacts | 160 ms |
| Ambient / sustained | Experimental soft texture during held bass | 180 ms attacks; 280–650 ms texture |

These are manually selected tuning presets, not automatic scene recognition. Movies cannot distinguish dialogue from music, and Games cannot identify footsteps, shots or other gameplay events. Only Ambient enables sustained texture. The new tunings have synthetic validation; their tactile quality needs listening tests on the mouse. Adjust sensitivity for the source volume and mix.

## Custom profiles

- Choose a built-in or custom profile, enter a name, then **Duplicate selected** to save a separate copy of that profile's saved tuning.
- Adjust the live controls and use **Save current as new** to store those values under a new name.
- Select one of **Your profiles**, edit its name and/or the live controls, then **Update selected custom** to replace that saved profile. Built-ins cannot be overwritten.
- **Apply profile** loads a profile's tuning and preserves the current paused/enabled state. Duplicating or saving a profile alone does not apply it. Later live adjustments do not automatically overwrite the saved profile.

Custom profiles also appear in the Options+ profile action when its selection list is opened again. Renaming/updating keeps a stable ID, so assigned actions continue to work. Names must be unique (including built-ins, ignoring case), nonblank and at most 64 characters. Up to 32 custom profiles can be stored. Use a different name when saving another copy.

The SDK stores the versioned custom catalog separately under `CustomAudioProfilesV1`, with online backup disabled. Playback enabled state is not restored from a profile. Concurrent tabs cannot overwrite a newer catalog: use **Reload saved settings** after a conflict. Failed writes preserve existing profiles; unreadable or newer stored documents are preserved and profile writes are disabled for that run.

## Preferences and local endpoint

Preferences are a versioned JSON string under SDK key `AudioSettingsV1`, using `TryGetPluginSetting` and `SetPluginSetting(..., backupOnline: false)`. Writes occur outside the audio callback lock; live state changes only after a successful save. On first load without SDK preferences, the plugin imports `audio-settings.user.json` from `GetPluginDataDirectory()`. That legacy file remains intact for rollback, and is no longer updated after migration. Invalid/newer SDK documents are logged and preserved.

The browser server starts with the plugin. It directly attempts a randomly chosen port in **49152–65535**, retrying up to 32 times if binding fails, including collisions. It never probes and releases a supposedly free port. Exhaustion is logged without stopping audio capture; the Open haptic settings action retries. Unloading closes the listener and marks the launcher as stopped. The obsolete `EnableDebugServer` preference is ignored.

The endpoint accepts only loopback clients using its exact `127.0.0.1:port` authority. All API reads and writes require a random 256-bit session token, and foreign origins are rejected. The token is passed in a URL fragment, removed from the displayed URL, retained in the browser tab's session storage, and never included in public HTML or logs. JSON bodies are size-limited with a read timeout. Random ports handle allocation; authentication and request checks provide access control. Windows HttpListener uses shared HTTP.sys infrastructure, so its kernel socket listing is not proof of application-level network access.

## How the algorithm works

Each audio channel has independent band-pass filters centered at 100 Hz and 2 kHz by default. Channel energies are combined after filtering, so opposite-phase stereo retains its bass. The high center is capped at 40% of the input sample rate. These are overlapping bands, not instrument recognition.

CPAL captures the default output device through WASAPI on Windows and CoreAudio taps on macOS. It targets a 20 ms buffer (clamped to the device-supported range), falling back to the device default only when that configuration is unsupported. A native condition variable wakes the consumer on audio arrival. At most 40 ms of PCM is retained; overflow drops the oldest whole frames and resets detector continuity. Audio age includes the backend timestamp estimate and time spent waiting for the consumer. These estimates are not measurements of physical mouse latency. If CPAL initialization fails on Windows, the existing NAudio event-driven and then polling adapters are tried; the panel shows the fallback reason.

RMS is measured in approximately 5 ms windows. An onset can be detected by either a fast envelope crossing the adaptive background threshold, or a sufficiently large rise over approximately 20 ms. The second route helps detect a beat over an already-playing bass note. Hysteresis and a per-band refractory interval prevent repeated detection of one attack.

The scheduler chooses one fresh candidate per audio callback. Actual attacks take priority over sustained texture; relative strength breaks ties. All sends, including manual previews, share the playback gate. Stale, weaker, and cooldown-blocked candidates are discarded rather than queued. Settings changes preserve scheduler counters and spacing.

The supported texture palette is `subtle_collision`, `damp_collision`, `sharp_collision`, `damp_state_change`, `sharp_state_change`, and `wave`. The original three event names are preserved, and six additional preset events allow runtime selection without rewriting YAML.

### Using presets to suggest vibration

For music, start with rounded bass impacts and quiet detail taps. Reserve a stronger preset for stronger bass attacks. Leave space between pulses so that the rhythm is perceptible. Wave/transition patterns may run longer; compare them with the preview controls before using them for frequent attacks.

The optional sustained mode waits for at least 200 ms of bass energy, then emits soft or damped pulses. Energy above its threshold maps to a spacing between 260 ms (quiet) and 140 ms (loud) by default. It follows the measured envelope, never schedules a free-running repeating timer, and yields to beat attacks. It decays with the audio envelope when sound stops. It is a tactile texture, not audio waveform reproduction or a beat/BPM tracker, and can introduce a pulse rate unrelated to the music's tempo. Off is the cleaner default for rhythm.

Logitech events do not expose per-event motor frequency, amplitude, playback completion, or a stop command in the documented mapping interface. The shared spacing limits sends but cannot guarantee that a selected preset has finished. Pausing prevents new audio events; it does not cancel a preset already playing. Overall device intensity remains a Logi Options+ setting.

## Diagnostics

- The browser panel includes live audio graphs and response timing. `/settings` and `/metrics` require the session token.
- `SentCount` counts successful calls to the event API, not confirmed physical vibrations.
- `DroppedCount` counts candidates skipped by age, priority or spacing (including backend exceptions).
- `AudioReceived` distinguishes initial placeholders from actual audio. `Timestamp` is the most recent audio sample; it stops advancing during a packet gap. `LastSentUtc` includes manual previews.
- The detector resets after a packet gap over 250 ms or a capture discontinuity. After changing output devices, a capture failure or granting Mac permission, use **Retry audio capture**. Saved settings and manual previews remain available when capture is unavailable.

The control server binds only localhost/127.0.0.1. Writes require its page token, same origin when an Origin header is supplied, validated JSON, and a bounded request body. Controls do not expose file paths to the browser or save audio recordings.

### Investigating delayed pulses

Expand **Response timing** to see capture mode, audio batch size, detector execution time, callback lock wait, and Logitech API call duration. Latest/maximum values reset on reload. CPAL event-age checks include its estimated capture age plus processing and lock wait; the NAudio fallback has no upstream capture timestamp. The panel also reports the newest sample age and discarded capture frames. The API call duration ends when `RaiseEvent` returns and excludes subsequent Logitech service, transport, or motor playback. No metric here measures physical end-to-end latency.

If the event counter increases before a vibration is felt, compare isolated preview taps with music. A useful sparse comparison is bass-only, both bass textures set to Soft tap, sustain off, and 160 ms spacing. This is an experiment, not a documented device throughput limit. The plugin drops cooldown-blocked candidates immediately; increasing spacing does not put them in a delayed queue. Logitech playback after dispatch remains unobservable through this interface.

To compare original NAudio capture batching against the new implementation on your default endpoint:

```powershell
dotnet run --project tools/CaptureTiming -c Release
```

This Windows-only diagnostic compares original NAudio, responsive NAudio and the packaged CPAL bridge for six seconds, keeps the endpoint active with a silent output stream, and prints timing percentiles without storing audio. An optional argument supplies a different plugin binary directory. It does not send haptics itself; an already-running plugin can still respond to other audio. A local run on 2026-09-06 measured median batches of 60 ms for the original polling capture versus 10 ms for event-driven capture (95th percentiles 70 ms versus 10 ms). The CPAL bridge was subsequently measured on the same machine at 10 ms median and 95th percentile batches, with no capture errors or discarded frames during the six-second comparison. This establishes capture delivery timing on that machine, not physical haptic latency.

## Build, test, and package

Install .NET 8 SDK or newer, Python 3, Rust (the build uses the pinned 1.90.0 toolchain), a native linker (Visual Studio C++ build tools on Windows or Xcode tools on Mac), and Logi Options+ / Logi Plugin Service. `PluginApi.dll` is referenced from the installed host. NAudio remains at 2.2.1 to match the host-supplied runtime assemblies. The capture subclass is built into HapticAudioCapture.dll so package inspection can validate the plugin while runtime reuses the host NAudio assemblies. The Mac package uses a separate `bin-mac` directory with the portable NAudio.Core DSP assembly. Do not bundle private Windows copies of NAudio.Core.dll or NAudio.Wasapi.dll: duplicate COM interop types caused an InvalidCastException during live reload.

```powershell
cargo +1.90.0 test --manifest-path native/cpal-capture/Cargo.toml --locked
dotnet run --project tests/CaptureBridge -c Release
dotnet run --project tests/AudioRegression -c Release
dotnet run --project tests/NativeControls -c Release
dotnet run --project tests/BrowserSettings -c Release
dotnet build HapticAudioFeedbackPlugin.sln -c Release -p:DeployPlugin=false
python tools/pack_plugin.py ./bin/Release/ ./bin/HapticAudioFeedbackPlugin.lplug4
logiplugintool verify ./bin/HapticAudioFeedbackPlugin.lplug4
```

Omit `-p:DeployPlugin=false` for the existing development workflow that writes the link and asks the host to reload the plugin. CI disables deployment and runs the regression suite. The assembly is named `HapticAudioFeedbackPlugin.dll` for package validation; plugin identity remains `HapticAudioFeedback`.

The host can load assembly bytes, leaving `Assembly.Location` empty. Settings and UI paths therefore use the SDK's `AssemblyFilePath`. This update does not install a new SDK/Options+ version or update mouse firmware.

## macOS capture and packaging

The .NET adapter starts `Feel the Rhythm Capture.app` directly, without a shell or another HTTP endpoint. Its `Info.plist` (also embedded in the executable) contains the system-audio usage description. PCM passes through a private parent/child pipe and is never saved. Framed timestamps allow the parent to discard stale pipe backlog. Closing the plugin terminates its helper; crashes and permission errors are reported in the settings panel. macOS may attribute the permission to the responsible parent application; confirm the actual prompt on the target Mac.

Grant the system audio recording permission in macOS Privacy & Security, then use **Retry audio capture**. No microphone or virtual audio driver is used. CPAL 0.18.2 treats combined CoreAudio input/output devices as capture inputs, so this adapter rejects them to avoid recording a microphone. Choose a separate output device, such as built-in speakers. Automatic output switching is not promised; retry after changing devices.

On a Mac, install the relevant targets and build the helper assets:

```sh
rustup toolchain install 1.90.0 --profile minimal
rustup target add --toolchain 1.90.0 aarch64-apple-darwin x86_64-apple-darwin
python3 tools/build_cpal.py --target aarch64-apple-darwin --output native/prebuilt
python3 tools/build_cpal.py --target x86_64-apple-darwin --output native/prebuilt
```

Ordinary plugin builds generate native assets for the build host. `-p:BuildCpalNative=false` consumes existing `native/prebuilt` assets; `-p:RequireAllCpalAssets=true` requires Windows x64 and both Mac architectures. Mac support is enabled in the generated manifest only when Mac helper assets are present. A local Windows-only package therefore does not advertise Mac support. CI builds the three targets separately and merges them before packing. The packaging wrapper preserves the Mac helper executable bits in the archive; verify installation and helper launch on Mac.

The build script applies an ad-hoc development signature. Public Mac distribution still requires a stable Developer ID signing/notarization workflow and testing of permission persistence across upgrades. Never modify the installed Logitech application bundle to supply our permission string.

The host can watch the development output even with `DeployPlugin=false`. For isolated validation while the plugin is loaded, build with `-p:BaseOutputPath=E:/HapticAudioFeedbackPlugin/bin/validation/` on this Windows checkout. Native DLLs stay locked while loaded; install the new package or stop the development plugin before overwriting its native binary.

## Third-party licenses

The capture dependency is pinned to CPAL 0.18.2 with optional backends disabled. `Cargo.lock` is checked in. `tools/audit_cpal_licenses.py` checks the resolved Windows/macOS binary dependency closures against MIT/Apache-2.0 choices and generates the shipped notices under `licenses/`. Rust procedural macros and build-only dependencies are classified separately because they are not bundled; this is not a license audit of the compiler, OS frameworks or proprietary Logitech SDK.

Some crates omit license text from their registry archive. `native/licenses` preserves the declaration from their exact upstream source revision; the objc2 MIT text is supplemented from upstream's published license, with package authors retained. The upstream objc2 declaration also records its Apple SDK licensing caveat, which is preserved in the notices. NAudio 2.2.1's MIT notice is included separately. No GPL component is added.

## Verification

The synthetic regression suite covers stereo phase, filtering, sample rates, callback boundaries, sustained sounds, beats over a sustained baseline, sensitivity, waveform selection, optional pulse density, disabled bands, scheduling priority, cooldown, malformed inputs and saved settings. These tests never capture audio or drive hardware.

SDK action tests use the installed SDK with a simulated controller to verify action dispatch and dropdown selection. Browser integration tests exercise real local HTTP listeners, collision retries, authentication, origin/host restrictions, validation, stale saves and cleanup. Run HTTP listener tests under a normal user account; the restricted Windows sandbox cannot initialize HTTP.sys handles. Live verification must separately check host loading, saved preferences, browser interaction, and the old fixed port being closed. A successful SDK call does not prove a physical vibration.
Official references:

- [SDK settings storage](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/managing-plugin-settings/)
- [Native Action Editor controls](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/action-editor-actions/)

- [Logitech event/waveform interface](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/)
- [Logitech haptics best practices](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-best-practices/)
- [Windows haptics and focus requirements](https://learn.microsoft.com/en-us/windows/apps/develop/input/haptics)
- [CPAL 0.18.2 capture backends](https://github.com/RustAudio/cpal/tree/v0.18.2)
- [NAudio 2.2.1 capture defaults and polling loop](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiCapture.cs)
- [Windows loopback recording and event support](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)

### Choose an audio source

In browser settings, select **Audio source**, then **Use device**. Playback devices capture the sound sent to speakers/headphones; input devices capture a microphone, line-in, or virtual input. **System default playback device** retains the original behavior. The system default is resolved when capture starts; use **Retry audio capture** after changing the OS default. **Refresh devices** rescans connected devices without opening a recording stream.

The choice is saved with SDK settings using CPAL's stable device ID (not a list index or display name), and remains unchanged when applying or saving haptic profiles. A missing or unsupported explicitly selected device produces a capture error; it never falls back to another source. Microphone/input capture is opt-in and subject to OS recording permissions. Audio is processed in memory and is not recorded or uploaded.

macOS uses the same selector through its CPAL helper. CPAL 0.18.2 cannot safely loop back combined input/output CoreAudio devices, so those are omitted from playback choices; their input can be chosen explicitly. Real Mac device/permission testing remains required.

### GitHub CI

The `Build and Package Plugin` workflow runs for pull requests, pushes to `main`/`master`, and manual dispatches. Jobs have time limits and read-only repository permissions; a new run cancels an older run for the same branch or pull request.

- **Workflow validation** runs actionlint 1.7.12.
- **Regression checks** runs the audio, authenticated browser API, browser device selector, and package validation tests without installing the SDK or opening audio devices.
- **Native** runs Rust tests with an explicit target on matching Windows x64, macOS 15 Apple Silicon, and macOS 15 Intel runners. It also tests the managed capture protocol, builds the native assets, and checks the Mac helper's development signature.
- **Build and validate package** waits for those checks, combines all three native archives, installs LogiPluginTool **6.1.4.22672** and the existing pinned SDK installer, then builds with deployment disabled. It runs SDK action checks, SDK package verification, and additional checks for native architectures, Mac executable permissions, required payloads and license notices.
- **CI** is the final aggregate check. It fails if any required job failed, was cancelled, or was skipped. Select this check in GitHub branch protection when configuring required checks; adding the workflow does not change repository protection settings.

A successful run uploads `Feel-the-Rhythm-<commit SHA>` containing the combined `.lplug4` package (14-day retention). Intermediate native archives are retained for seven days. These are development artifacts; the workflow does not publish a release or notarize the Mac helper. Native tests do not validate real audio hardware, permission prompts, or physical haptic latency.

Local checks for the CI additions:

```powershell
python -m unittest discover -s tests/Packaging -v
python tools/verify_package.py ./bin/HapticAudioFeedbackPlugin.lplug4
# Require Windows x64 plus both Mac architectures for a combined package:
python tools/verify_package.py ./HapticAudioFeedbackPlugin.lplug4 --require-all
# With Go 1.25+ installed:
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12
```
