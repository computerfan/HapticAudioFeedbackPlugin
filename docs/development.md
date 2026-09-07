# Developer guide

[Back to README](../README.md)

## Build and package

Prerequisites:

- .NET 8 SDK or newer, Python 3, and Rust with the pinned 1.90.0 toolchain.
- Visual Studio C++ build tools on Windows, or Xcode command-line tools on macOS.
- Logi Options+ / Logi Plugin Service, which supplies `PluginApi.dll`.
- LogiPluginTool 6.1.4.22672 for packaging.

Run from the repository root:

```powershell
rustup toolchain install 1.90.0 --profile minimal
dotnet tool install --global LogiPluginTool --version 6.1.4.22672
dotnet build HapticAudioFeedbackPlugin.sln -c Release -p:DeployPlugin=false
python tools/pack_plugin.py ./bin/Release/ ./bin/HapticAudioFeedbackPlugin.lplug4
python tools/verify_package.py ./bin/HapticAudioFeedbackPlugin.lplug4
```

The build copies the root MIT license and dependency notices into the package. The packaging wrapper runs SDK verification and preserves Mac helper executable permissions.

Omitting `-p:DeployPlugin=false` writes the development link and requests a plugin reload. The host may still watch an existing development link when deployment is disabled. To validate without replacing loaded binaries, choose a separate output directory:

```powershell
dotnet build HapticAudioFeedbackPlugin.sln -c Release -p:DeployPlugin=false "-p:BaseOutputPath=$($PWD.Path)/bin/validation/"
```

A loaded Windows native DLL cannot be overwritten; stop the development plugin before replacing it.

## Tests

```powershell
cargo +1.90.0 test --manifest-path native/cpal-capture/Cargo.toml --locked
dotnet run --project tests/CaptureBridge -c Release
dotnet run --project tests/AudioRegression -c Release
dotnet run --project tests/NativeControls -c Release
dotnet run --project tests/BrowserSettings -c Release
node tests/BrowserSettings/device-ui.test.cjs
python -m unittest discover -s tests/Packaging -v
```

SDK action tests require the installed SDK and use a simulated controller. Browser tests create real loopback listeners; run them under a normal Windows account with access to HTTP.sys. The default test commands do not open audio devices or send haptic events.

The capture bridge has an optional `--device-smoke <plugin-bin-directory>` mode for live Windows device enumeration and playback capture checks. Real hardware testing must separately verify audio permissions, profile usability, and physical haptic response.

## Experimental macOS assets (unsupported)

Windows x64 is the only officially supported platform and the current development priority. CI continues building and testing both Mac architectures; Mac payloads remain available for experimental testing.

The experimental adapter launches `Feel the Rhythm Capture.app` through LaunchServices and transfers PCM over a private Unix socket. Permission attribution and live capture remain unverified; see [the investigation](macos-capture-diagnostics.md). Once connected, the socket name and private session directory are removed before waiting for permission. On parent disconnect, the native helper requests normal shutdown and exits after a 750 ms grace period if capture is still blocked. Managed shutdown cancels reads without joining callbacks; device enumeration uses the same bounded process supervisor as Windows. A host crash before socket acceptance can still leave a small temporary directory; this change does not sweep existing temporary files.

On a Mac:

```sh
rustup toolchain install 1.90.0 --profile minimal
rustup target add --toolchain 1.90.0 aarch64-apple-darwin x86_64-apple-darwin
python3 tools/build_cpal.py --target aarch64-apple-darwin --output native/prebuilt
python3 tools/build_cpal.py --target x86_64-apple-darwin --output native/prebuilt
```

Ordinary builds generate native assets for the build host. `-p:BuildCpalNative=false` uses existing assets from `native/prebuilt`; `-p:RequireAllCpalAssets=true` requires Windows x64 and both Mac architectures. The generated manifest includes Mac compatibility when helper assets are present; this enables experimental installation, not an official support guarantee.

The helper uses an ad-hoc development signature. Public Mac distribution requires a Developer ID signing/notarization process and permission-persistence testing. Do not modify the installed Logitech application bundle.

CPAL 0.18.2 uses the real input on combined CoreAudio input/output devices. The selector omits those devices from playback-loopback choices to avoid unintended microphone capture; their input remains available for explicit selection.

## Capture and detection

CPAL 0.18.2 provides WASAPI capture on Windows and CoreAudio capture on macOS. Device choices use stable, direction-qualified IDs. The system default is resolved when capture starts. An unavailable explicitly selected device fails without switching sources.

The native engine requests a 20 ms buffer, adjusted to supported limits, and retries with the device default only for unsupported configurations. Its bounded queue retains at most 40 ms of PCM; overflow discards old frames and marks a discontinuity. Audio callbacks do not call managed code or wait for consumers. Windows capture and device enumeration run in an owned helper process. Startup and enumeration have 10-second deadlines; shutdown allows 250 ms for normal exit, then terminates a stalled helper and waits at most one further second. Startup runs outside the plugin lifecycle thread. Failures are shown in settings for retry; there is no automatic in-process fallback that could block the host on a stalled driver.

Haptic SDK calls run on one background worker outside the audio/metrics lock. Only one pulse can be accepted at a time; incoming pulses are dropped while it is busy. Before dispatch, age and settings/capture identity are checked again. Counters and chart markers update after successful SDK return, with cooldown measured from completion to avoid a burst after a slow call. Pause/reconfiguration cancels work that has not started; an SDK call already in progress cannot be cancelled. Unload does not wait for a blocked call, and never creates a replacement worker for it.

The detector filters each channel independently into bass and detail bands, then combines their energies. Defaults are 100 Hz and 2 kHz; the detail center is capped at 40% of the sample rate. Approximately 5 ms RMS windows feed fast envelopes and slower background tracking. Threshold crossings or a rapid rise can trigger an onset; hysteresis and per-band re-arm intervals suppress repeated triggers.

The scheduler selects one fresh candidate per callback. Attacks take priority over sustained texture, and previews share the same spacing limit. Stale, weaker, and cooldown-blocked candidates are discarded. Settings changes preserve counters and spacing while allowing 400 ms for detection to settle.

Sustained texture follows held bass energy with spaced soft pulses. It is neither waveform reproduction nor BPM tracking. The event interface does not report physical playback completion; send spacing cannot guarantee a preset has finished. Pausing stops new automatic events, not a preset already playing.

NAudio remains at 2.2.1 to match the host runtime. The capture adapter is a separate assembly for SDK package inspection. Windows reuses host NAudio assemblies; do not package private copies of `NAudio.Core.dll` or `NAudio.Wasapi.dll`. The separate Mac managed directory includes portable `NAudio.Core.dll` for DSP.

### Haptic device targeting

Checked on 2026-09-06 against the installed Plugin API **6.2.6.1611** and the [SDK haptics documentation](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/). The public `PluginEventSender` exposes `RaiseEvent(string eventName)` with no target-device parameter. No public API for selecting an individual Logitech haptic output was found. Device IDs exposed for incoming control events do not provide a haptic targeting mechanism.

The [waveform mapping](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/#waveform-mapping) keys select presets by model name, with a `DEFAULT` fallback. They cannot distinguish two physical devices of the same model.

The plugin sends each event once and shares its audio source, tuning, profiles, and data folder across devices. Logi Options+ determines delivery. The reviewed documentation does not establish whether all connected haptic devices respond or only an active device, and this behavior has not been tested with two devices. `SentCount` counts successful event API calls, not receiving devices or confirmed vibrations. The browser's **Audio source** selector controls capture only.

Do not expose a haptic output selector until a supported targeting API is available. Recheck this limitation when upgrading the SDK; determining current multi-device playback behavior requires a two-device test or confirmation from Logitech.

## Settings and local browser

See [Plugin data folder](../README.md#plugin-data-folder) for the Windows path and instructions for opening it. The plugin obtains the actual directory from the SDK's [`GetPluginDataDirectory()`](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/storing-plugin-data/). It places `Open Haptic Settings.html` and the `logs` subfolder there. The Windows location has been verified locally; the expected Mac path combines the [documented service directory](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/introduction/) with the plugin-data layout and still needs Mac verification.

The SDK stores preferences under `AudioSettingsV1` and custom profiles under `CustomAudioProfilesV1`, with online backup disabled. Legacy preferences are imported once when SDK settings are absent. Save failures preserve live state; invalid or newer stored documents are retained. Paths use the SDK's `AssemblyFilePath`, because the host may load assemblies without an `Assembly.Location`.

Profiles exclude the selected audio source and playback-enabled state. The custom catalog allows 32 entries with unique names of up to 64 characters. Stable profile IDs keep assigned actions valid after renaming. Settings and catalog writes use separate revision checks to reject stale browser drafts.

The browser binds directly to a random port in 49152–65535, retrying up to 32 collisions. It accepts only loopback clients with the exact authority. API access requires a random 256-bit session token and rejects foreign origins. Requests have JSON validation, size limits, and a read timeout.

The launcher passes the token in a URL fragment; the page removes it from the displayed URL and retains it in tab session storage. Tokens are excluded from public HTML and logs. The launcher itself contains the token and should remain private. Unloading closes the listener and marks the launcher stopped.

## Diagnostics

The browser's **Diagnostics** panel shows capture batches, processing time, callback lock waits, backend call duration, and estimated sample age. `/metrics` and `/settings` require authentication.

- `SentCount` counts successful event API calls, not confirmed physical vibrations.
- `DroppedCount` counts candidates skipped by age, priority, spacing, or backend failure.
- `AudioReceived` and `Timestamp` identify actual received audio; `LastSentUtc` includes previews.
- Capture gaps over 250 ms or reported discontinuities reset detector state.

For a live Windows capture comparison:

```powershell
dotnet run --project tools/CaptureTiming -c Release
```

This compares original NAudio, responsive NAudio, and the packaged CPAL bridge on the default endpoint. It uses a silent output stream to keep capture active and reports timing without saving audio or sending haptic events itself. An already-running plugin can still respond to other audio. These measurements exclude physical device playback latency.

### Bounded logging and counters

Runtime diagnostic logs live in the plugin data directory's `logs` folder: `feel-the-rhythm.log` and two rotated archives. Each file is capped at 512 KiB (1.5 MiB total). Rotation touches only those three filenames. The writer uses a 64-entry queue, retains at most 2,048 characters per message, and accepts at most 30 entries per 60-second window. Duplicate messages are suppressed within that window; the next accepted entry summarizes suppressed messages. Audio callbacks never wait for disk I/O. There are no per-frame log writes.

Storage failures do not interrupt capture or settings. The writer backs off for 60 seconds, and the browser's Diagnostics panel shows logging failures and suppressed counts. Experimental Mac capture startup errors use a bounded socket response; enumeration stderr retains at most 4,096 characters. Logitech's own host logs have separate retention outside this plugin's control; the plugin sends one log-location entry to the SDK per load.

Sent, dropped, and suppressed counters saturate at their 64-bit limits instead of wrapping. The metrics API sends counters as decimal strings so JavaScript does not lose precision above `2^53 - 1`. The browser uses `BigInt`, compact labels, and exact totals in tooltips; a `+` marks a saturated counter. The chart retains at most 2,560 detector frames and 256 onset markers. Settings and profile revisions reject further writes at their limit while preserving existing data.

Run the logging failure and size-limit checks with:

```powershell
dotnet run --project tests/Robustness -c Release
```

## GitHub CI

[Build and Package Plugin](../.github/workflows/build-and-pack.yml) runs for pull requests, pushes to `main`/`master`, and manual dispatches. Jobs have read-only repository permissions and time limits; newer runs cancel older runs for the same branch or pull request.

| Job | Checks |
| --- | --- |
| Workflow validation | actionlint 1.7.12 |
| Regression checks | Audio, browser API/UI, bounded logging and counters, package fixtures, Python syntax |
| Native | Explicit-target Rust tests and managed protocol tests on Windows x64, Apple Silicon, and Intel Mac runners; native builds and Mac signature verification |
| Build and validate package | Combine all three native archives; build with deployment disabled; SDK action tests; SDK package verification; native architecture, permission, and license checks |
| CI | Aggregate result; fails if a required job failed, was cancelled, or was skipped |

LogiPluginTool is pinned to 6.1.4.22672. A successful run uploads `Feel-the-Rhythm-<commit SHA>` with the combined `.lplug4` test package for 14 days; native archives remain for seven days. Artifacts are development builds, not published or notarized releases. Select **CI** in branch protection when configuring required checks.

Additional local checks:

```powershell
# Combined test package must include Windows x64 and both Mac architectures:
python tools/verify_package.py ./HapticAudioFeedbackPlugin.lplug4 --require-all
# Requires Go 1.25+:
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12
```

### Publishing a release

[Release Plugin](../.github/workflows/release.yml) runs when a tag such as `v0.5.0` is pushed. The tag must be exactly `vMAJOR.MINOR.PATCH` and match `version` in `src/package/metadata/LoupedeckPackage.yaml`. Commit the version change and workflow before tagging. Ordinary branch pushes and manual CI runs do not publish releases.

For example, after committing version `0.5.0`:

```powershell
git push origin main
git tag -a v0.5.0 -m "Feel the Rhythm 0.5.0"
git push origin v0.5.0
```

The release reuses the full CI pipeline at the tagged commit. Only after all checks pass does it upload `Feel-the-Rhythm-0.5.0.lplug4` and publish a GitHub Release with generated notes. Windows x64 is officially supported. Mac payloads are included for experimental testing only, without official support. Native archives and other CI artifacts are not attached to the release. GitHub still supplies its automatic source-code ZIP and tarball links.

No extra secret is needed: only the publishing job receives `contents: write` through `GITHUB_TOKEN`. Uploads happen while the release is a draft, and existing releases are never overwritten. If building fails, rerun the failed jobs. If publishing fails after creating a draft, delete that unpublished draft in GitHub (keep the tag), then rerun the failed publishing job. If the release is already published, leave it intact and use a new version for corrections. Do not move published version tags. Rerun the whole workflow if its package artifact has expired.

## Licenses and references

### Browser frontend

The settings page uses **Pico CSS 2.1.1**, vendored as an unmodified 83,319-byte precompiled stylesheet. Pico is MIT licensed and has no runtime dependencies. Its npm build tools are not shipped. The remaining HTML, CSS, and JavaScript are project code under MIT; charts use Canvas 2D directly. System fonts and browser APIs are provided by the user's environment.

Every package includes the root `LICENSE`, `licenses/FRONTEND-NOTICES.txt`, Pico's full `licenses/Pico-CSS-MIT.txt`, and `licenses/FRONTEND-dependencies.json` with the pinned source, version, and SHA-256. Package verification checks that dependencies use MIT or Apache-2.0, their notice files exist, and their bytes match the inventory. The stylesheet is served through one fixed local GET route; no CDN, arbitrary file server, or external font requests are used. The CSP permits Pico's embedded SVG icons as image data URLs.

Selecting a profile immediately updates the visible values and queues a serialized save. One-step Undo restores the preceding tuning while retaining the current source and playback state. The selector identifies matching or modified tuning; built-in and saved profiles are never overwritten by live slider changes. Source changes retain explicit confirmation to avoid starting microphone capture on selection alone. Failed saves stop automatic retries and expose Retry saving / Reload saved settings. Related timing constraints remain valid, and disabled bands and sustained texture disable their dependent controls.

`dotnet run --project tests/BrowserSettings -c Release -- --preview` opens an isolated loopback test server for visual review with in-memory profiles and simulated controllers; it never captures audio or dispatches hardware events. Stop the process after review. UI tests exercise profile selection, Undo, pending saves, failures, coupled constraints, and custom profile operations.

When adding a frontend library or redistributed asset, review its dependency tree for MIT or Apache-2.0 choices and include the required full license texts and notices. Update the inventory and pinned checksums; automated presence/integrity checks do not replace reviewing a new dependency's license.

### Other components and references

The project uses the [MIT License](../LICENSE). `tools/audit_cpal_licenses.py` audits the locked Windows/macOS binary dependency closures for MIT or Apache-2.0 alternatives and collects notices. Compiler tools, procedural macros, and build-only dependencies are classified separately. The audit does not cover the proprietary Logitech SDK or OS frameworks.

Pinned upstream declarations for crates missing notice files are retained in [native/licenses](../native/licenses/README.md), including the objc2 Apple SDK caveat. NAudio's MIT notice is shipped separately.

- [SDK settings storage](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/managing-plugin-settings/)
- [Action Editor controls](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/action-editor-actions/)
- [Logitech haptic events](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/)
- [Haptics best practices](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-best-practices/)
- [CPAL 0.18.2](https://github.com/RustAudio/cpal/tree/v0.18.2)
- [NAudio 2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1)
- [Windows loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)


## Localization

Simplified Chinese uses the SDK language ID `zh-CN`. Action names/descriptions and the action group are translated by `src/package/localization/HapticAudioFeedback_zh-CN.xliff`, generated from the running plugin using `logiplugintool xliff HapticAudioFeedback <output-directory>`. Keep the generated IDs and English source strings stable. Options+ selects its language automatically when supported, or the user can choose a plugin language in plugin settings; restart Logi Plugin Service after installing locale files.

`src/package/ui/locales/zh-CN.json` contains browser strings and runtime action-editor labels/profile descriptions. It is embedded into the assembly for native list items, and included in the package for the browser. Names supplied by the user and device names are displayed unchanged. Low-level operating-system/backend diagnostics retain their original wording when there is no translation.

The browser uses its preferred supported language, falling back to English. Its language selector changes only presentation and records `?lang=en` or `?lang=zh-CN` in the current URL; refreshing preserves that choice. Opening the launcher after a plugin restart uses browser preferences again because the local port changes. The browser language and Options+ plugin language are independent. No audio settings are written when switching languages.

Only `/localization.js` and `/locales/zh-CN.json` are public localization routes; settings and device endpoints still require the session token. The locale adds no third-party dependencies.


### Tabbed settings and onset chart

The live overview sits outside the three tab panels. Built-in profile selection and expandable custom-profile management share the first (Tune) tab. Tabs use ARIA tab/tabpanel roles, roving focus, and arrow/Home/End keyboard navigation. Switching tabs hides existing controls instead of recreating them, preserving drafts, Undo, and pending audio-source selections. Chart scale is presentation-only and never writes audio settings.

The detector emits envelope/threshold readings for every completed analysis window (about 5 ms). `AudioTraceHistory` stores at most 2,560 frames in a fixed ring; a successful attack dispatch sets `SentBand` on that exact detector frame. Preview calls and sustained pulses are excluded. There is no raw audio in this telemetry. Capture timestamps approximate the audio clock, but a dot and its line vertex share one timestamp and one measured level. Resets clear the ring and set `BreakBefore` on the next frame; sequence counters continue across resets and serialize as decimal strings.

`RecentAudio` sends at most the latest 256 frames per metrics response, covering roughly 1.28 seconds at 200 Hz. This overlaps successive polls without retransmitting the full graph. The browser deduplicates by sequence plus timestamp and keeps at most 2,560 frames within 12 seconds; markers are derived only from those frames and capped at 256. Long polling interruptions create gaps rather than invented readings. Separate legacy `RecentOnsets` telemetry remains bounded for API compatibility, but the chart does not use it to interpolate or alter the line.

The chart breaks lines at capture resets or gaps over 50 ms, includes visible levels/thresholds in auto-scaling, expands immediately, and contracts gradually with a minimum 30 dB span. Silent sentinel values do not distort scaling. A fixed −80 to 0 dBFS mode remains available. Polling slows from 100 ms to 500 ms when the document is hidden. A dot confirms an onset was sent through the SDK; it does not confirm physical vibration. The dashed line is the adaptive level threshold, not the only trigger rule: rapid rises can trigger below it, and re-arm/spacing rules can skip crossings.

The detector records the qualifying trigger rule before changing its armed state. Sent trace frames carry `TriggerReason`: `threshold` draws a circle and `rise` draws a diamond. If both rules qualify, the level rule takes precedence; a rapid-rise marker means the rapid-rise rule was needed. Color continues to identify the band. Missing/unknown reasons draw hollow circles rather than being guessed from envelope/threshold positions. This annotation does not change onset selection, timing, or scheduling.

### Settings-to-chart connections

Tune groups shared sensitivity/spacing, mint bass controls, purple detail controls, and the two trigger rules. Band groups show the latest envelope and adaptive threshold, with stale data replaced by a dash and disabled bands labeled Off. The legend buttons navigate to the corresponding Tune group without saving settings or changing capture. Hover/focus emphasizes matching plotted lines or marker outlines; all original field IDs, auto-save, Undo, and explicit audio-source confirmation remain in place.

The monitor is sticky only above 900 CSS pixels wide and at least 600 pixels tall, with a shorter plot on screens up to 800 pixels tall. A ResizeObserver measures its actual height for anchor offsets, so legend navigation leaves the selected group below the plot. Smaller viewports use normal scrolling. Detail controls inherit purple slider colors with the same contrast treatment as the mint controls.

Browser requests keep an abort deadline active through response-body consumption: 3 seconds for live metrics and 15 seconds for settings, device lists, logs, translations and actions. Metrics polling retries with backoff from 1 to 10 seconds after failures and resumes normal polling after recovery. Writes are never automatically retried after a timeout; their result may be uncertain, so reload saved settings before retrying.

The live browser monitor pauses polling and canvas drawing while the document is hidden, then requests fresh metrics on return. Audio capture and haptics continue independently. A long absence appears as a gap rather than interpolated history. Overlapping metric batches reuse chart entries; sorting is only needed for newly arriving out-of-order points. The history and its lookup map stay capped at 2,560 points and markers at 256.


### Long-running validation

Run the optional accelerated browser history test with:

```sh
node --expose-gc tests/BrowserSettings/device-ui.test.cjs --soak
```

This feeds one simulated hour of overlapping detector frames through the real browser history code (36,000 polls), checking both history/index bounds and complete expiry. It does not run the browser renderer or audio hardware for an hour. On 2026-09-07, the local run peaked at 2,401 points and 25 markers, removed all expired entries, and reported 69,904 bytes of retained Node heap growth after warm-up. Heap measurements are diagnostic, not a portable performance threshold.

The 22 robustness checks also passed, including three-file log rotation, error-flood limits, storage failure recovery and saturating counters. Remaining validation is a real overnight Windows capture/haptics run and macOS permission-dialog cancellation. The SDK cannot cancel an already-running haptic call; a blocked call retains its worker until it returns. A Mac host crash before socket acceptance can leave a small temporary directory. Development build/cache directories are separate from runtime storage and are not covered by log rotation.
