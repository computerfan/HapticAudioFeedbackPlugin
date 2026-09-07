# macOS silent capture investigation (2026-09-07)

The Tahoe VM reported a 48 kHz stereo stream, zero haptic events and no System Audio Recording entry. This does not establish a working tap or a permission denial. The browser previously treated recent PCM packets (including all-zero PCM) as "Listening".

## Evidence and changes

- CPAL remains pinned to MIT/Apache-2.0 licensed 0.18.2. Its CoreAudio implementation creates a process tap and private aggregate device for output-only loopback. Duplex devices take its real input path; our existing guard rejects playback capture on those devices rather than silently selecting their microphone.
- Apple explains that a directly launched helper inherits its parent's responsibility. Our old `Process.Start(executable)` launch could therefore attribute recording to Logi's service despite the helper's own Info.plist. This is a likely cause, not yet confirmed by a TCC trace from the user's VM.
- Launch capture through `/usr/bin/open -n -a <bundle> --args ...`. Enumeration still runs directly and never starts capture. The helper connects to a random Unix socket in a mode-0700 directory under `/tmp`; no TCP port or audio file is added. Closing the connection stops capture, including when the plugin exits. Normal disposal removes the socket/directory; an abrupt host crash can leave an empty session directory/socket inode until temporary-file cleanup.
- Keep the existing NSAudioCaptureUsageDescription and microphone usage description. Do not reset TCC, modify Logi's bundle, use private permission APIs, or infer permission from zero samples.
- Startup errors use a bounded HCE1 response over the socket. Stream creation is logged as unverified. Live status separately reports missing packets, silent packets, and detected signal. Packet/sample counters saturate and are serialized as strings; signal diagnostics stay in memory, without per-packet logging.

## Follow-up: permission and installation

The user subsequently confirmed a permission prompt and working capture in the Tahoe VM. Installation could time out while waiting for a response. Capture startup now runs in the background: helper launch has a 30-second deadline, followed by up to five minutes for the capture handshake. Retry and unload cancel pending attempts.

The helper bundle identifier is now `space.cfan.feeltherhythm.capture`, using the developer-owned `cfan.space` domain. This changes the macOS permission identity, so an earlier grant may not carry over. The Logitech plugin name, action IDs and stored settings identity remain unchanged. The helper bundles the existing logo as an ICNS resource before signing.

Browser guidance reports denial only when CPAL explicitly returns PermissionDenied. Other failures and silence remain unknown; no private TCC preflight is used. The authenticated permissions button opens the relevant system settings pane, and Retry starts another capture attempt. Packet/sample counts and raw peak remain available in authenticated metrics but are removed from the live view.

## Required Mac validation

Both the managed capture assembly and native helper must be rebuilt together. Copying only the browser page or DLL cannot fix launch ownership. Windows x64 is supported; macOS support is experimental. CI builds and tests Intel and ARM Mac helpers and includes them in the combined package. These builds do not establish working Mac capture.

1. Install the rebuilt package and retry capture with an output-only device selected. Check that CaptureMode contains `via LaunchServices`.
2. Observe the system-audio permission prompt/entry for the helper and explicitly allow it. If it does not appear, inspect macOS Console's `tccd` messages for the responsible process; do not assume success.
3. Play audible audio through that selected output. The graph should respond and status should become Listening. If inspecting authenticated metrics, packet/sample counters should increase and raw peak should rise above -180 dBFS. Silence should show Silent audio; disconnected/stalled capture should not remain Listening.
4. Retry and reload repeatedly; confirm old helper processes exit. Quit Logi's service and verify no capture helper remains.
5. Verify denied permission, no output device, and microphone selection separately (microphone permission is a different privacy category).

Local Windows builds/protocol tests cannot validate LaunchServices, TCC prompts, USB audio in ESXi, or actual Mac loopback. The new bundle identity, icon, delayed installer response, and browser permission recovery still require a live Mac retest.

## Primary sources

- [Apple: launch responsibility](https://developer.apple.com/documentation/Security/applying-launch-environment-and-library-constraints)
- [Apple: system audio taps and permission](https://developer.apple.com/documentation/coreaudio/capturing-system-audio-with-core-audio-taps)
- [CPAL 0.18.2 CoreAudio source](https://docs.rs/crate/cpal/0.18.2/source/src/host/coreaudio/macos/loopback.rs)
- [CPAL discussion of silent capture and missing prompts](https://github.com/RustAudio/cpal/pull/894)
- [Upstream permission-check proposal](https://github.com/RustAudio/cpal/pull/1257) (not adopted; no unreviewed fork/private TCC API added).
