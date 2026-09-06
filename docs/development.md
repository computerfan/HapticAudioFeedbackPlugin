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

## macOS assets

The Mac adapter starts the bundled `Feel the Rhythm Capture.app` helper. Its embedded and bundled `Info.plist` declares system-audio and microphone usage. Audio passes through a private parent/child pipe, and plugin shutdown terminates the helper. macOS may attribute recording permission to the responsible parent application; verify the prompt on real hardware.

On a Mac:

```sh
rustup toolchain install 1.90.0 --profile minimal
rustup target add --toolchain 1.90.0 aarch64-apple-darwin x86_64-apple-darwin
python3 tools/build_cpal.py --target aarch64-apple-darwin --output native/prebuilt
python3 tools/build_cpal.py --target x86_64-apple-darwin --output native/prebuilt
```

Ordinary builds generate native assets for the build host. `-p:BuildCpalNative=false` uses existing assets from `native/prebuilt`; `-p:RequireAllCpalAssets=true` requires Windows x64 and both Mac architectures. The generated manifest advertises Mac support only when helper assets are present.

The helper uses an ad-hoc development signature. Public Mac distribution requires a Developer ID signing/notarization process and permission-persistence testing. Do not modify the installed Logitech application bundle.

CPAL 0.18.2 uses the real input on combined CoreAudio input/output devices. The selector omits those devices from playback-loopback choices to avoid unintended microphone capture; their input remains available for explicit selection.

## Capture and detection

CPAL 0.18.2 provides WASAPI capture on Windows and CoreAudio capture on macOS. Device choices use stable, direction-qualified IDs. The system default is resolved when capture starts. An unavailable explicitly selected device fails without switching sources. Windows default-playback capture can fall back to NAudio event-driven capture, then polling, if CPAL cannot initialize.

The native engine requests a 20 ms buffer, adjusted to supported limits, and retries with the device default only for unsupported configurations. Its bounded queue retains at most 40 ms of PCM; overflow discards old frames and marks a discontinuity. Audio callbacks do not call managed code or wait for consumers. A dedicated owner thread controls the Windows native stream's lifetime.

The detector filters each channel independently into bass and detail bands, then combines their energies. Defaults are 100 Hz and 2 kHz; the detail center is capped at 40% of the sample rate. Approximately 5 ms RMS windows feed fast envelopes and slower background tracking. Threshold crossings or a rapid rise can trigger an onset; hysteresis and per-band re-arm intervals suppress repeated triggers.

The scheduler selects one fresh candidate per callback. Attacks take priority over sustained texture, and previews share the same spacing limit. Stale, weaker, and cooldown-blocked candidates are discarded. Settings changes preserve counters and spacing while allowing 400 ms for detection to settle.

Sustained texture follows held bass energy with spaced soft pulses. It is neither waveform reproduction nor BPM tracking. The event interface does not report physical playback completion; send spacing cannot guarantee a preset has finished. Pausing stops new automatic events, not a preset already playing.

NAudio remains at 2.2.1 to match the host runtime. The capture adapter is a separate assembly for SDK package inspection. Windows reuses host NAudio assemblies; do not package private copies of `NAudio.Core.dll` or `NAudio.Wasapi.dll`. The separate Mac managed directory includes portable `NAudio.Core.dll` for DSP.

## Settings and local browser

The SDK stores preferences under `AudioSettingsV1` and custom profiles under `CustomAudioProfilesV1`, with online backup disabled. Legacy preferences are imported once when SDK settings are absent. Save failures preserve live state; invalid or newer stored documents are retained. Paths use the SDK's `AssemblyFilePath`, because the host may load assemblies without an `Assembly.Location`.

Profiles exclude the selected audio source and playback-enabled state. The custom catalog allows 32 entries with unique names of up to 64 characters. Stable profile IDs keep assigned actions valid after renaming. Settings and catalog writes use separate revision checks to reject stale browser drafts.

The browser binds directly to a random port in 49152–65535, retrying up to 32 collisions. It accepts only loopback clients with the exact authority. API access requires a random 256-bit session token and rejects foreign origins. Requests have JSON validation, size limits, and a read timeout.

The launcher passes the token in a URL fragment; the page removes it from the displayed URL and retains it in tab session storage. Tokens are excluded from public HTML and logs. The launcher itself contains the token and should remain private. Unloading closes the listener and marks the launcher stopped.

## Diagnostics

The browser's **Response timing** panel shows capture batches, processing time, callback lock waits, backend call duration, and estimated sample age. `/metrics` and `/settings` require authentication.

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

Storage failures do not interrupt capture or settings. The writer backs off for 60 seconds, and the browser's Response timing panel shows logging failures and suppressed counts. Mac helper stderr is continuously drained with only its first 4,096 characters retained. Logitech's own host logs have separate retention outside this plugin's control; the plugin sends one log-location entry to the SDK per load.

Sent, dropped, and suppressed counters saturate at their 64-bit limits instead of wrapping. The metrics API sends counters as decimal strings so JavaScript does not lose precision above `2^53 - 1`. The browser uses `BigInt`, compact labels, and exact totals in tooltips; a `+` marks a saturated counter. The chart retains only 120 samples. Settings and profile revisions reject further writes at their limit while preserving existing data.

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

LogiPluginTool is pinned to 6.1.4.22672. A successful run uploads `Feel-the-Rhythm-<commit SHA>` with the combined `.lplug4` package for 14 days; native archives remain for seven days. Artifacts are development builds, not published or notarized releases. Select **CI** in branch protection when configuring required checks.

Additional local checks:

```powershell
# Combined package must include Windows x64 and both Mac architectures:
python tools/verify_package.py ./HapticAudioFeedbackPlugin.lplug4 --require-all
# Requires Go 1.25+:
go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12
```

## Licenses and references

The project uses the [MIT License](../LICENSE). `tools/audit_cpal_licenses.py` audits the locked Windows/macOS binary dependency closures for MIT or Apache-2.0 alternatives and collects notices. Compiler tools, procedural macros, and build-only dependencies are classified separately. The audit does not cover the proprietary Logitech SDK or OS frameworks.

Pinned upstream declarations for crates missing notice files are retained in [native/licenses](../native/licenses/README.md), including the objc2 Apple SDK caveat. NAudio's MIT notice is shipped separately.

- [SDK settings storage](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/managing-plugin-settings/)
- [Action Editor controls](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/action-editor-actions/)
- [Logitech haptic events](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-getting-started/)
- [Haptics best practices](https://logitech.github.io/actions-sdk-docs/csharp/haptics/haptics-best-practices/)
- [CPAL 0.18.2](https://github.com/RustAudio/cpal/tree/v0.18.2)
- [NAudio 2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1)
- [Windows loopback recording](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)
