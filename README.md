<img src="src/package/metadata/Icon256x256.png" width="80" height="80" alt="Feel the Rhythm logo">

# Feel the Rhythm

Turn music and other audio into haptic feedback on compatible Logitech devices. Follow bass hits and instrument attacks with adjustable taps, impacts, and sustained textures.

## Features

- Choose system playback, a microphone, line-in, or a virtual audio input.
- Adjust sensitivity, bass/detail response, pulse spacing, and haptic textures.
- Start with profiles for music, movies, games, and ambient listening.
- Duplicate profiles and save your own tuning.

## Compatibility

Requires Logi Options+ and a Logitech device with supported haptic mappings.

| Platform | Status |
| --- | --- |
| Windows x64 | Tested locally |
| macOS 14.6+ · Apple Silicon and Intel | Experimental; hardware and recording permissions still need validation |

**Multiple haptic devices:** settings are shared, and the plugin cannot select an individual haptic output device. Logi Options+ controls event delivery; whether multiple connected devices vibrate together is unverified. The **Audio source** selector chooses the audio to analyze. See [SDK limitations](docs/development.md#haptic-device-targeting).

## Get started

1. Install a plugin package built for your platform. To build one, see the [developer guide](docs/development.md).
2. In Logi Options+, assign **Open haptic settings** to an Actions Ring slot and activate it.
3. Choose an **Audio source** and press **Use this source**. Select a **Listening profile** and play some audio. Profile changes apply immediately; **Undo profile change** restores your previous tuning.

You can also open **Open Haptic Settings.html** from the [plugin data folder](#plugin-data-folder). Reopen this launcher after a plugin restart instead of bookmarking the temporary browser address.

### Plugin data folder

**Windows:** paste this into File Explorer's address bar or **Win+R**:

```text
%LOCALAPPDATA%\Logi\LogiPluginService\PluginData\HapticAudioFeedback
```

**macOS:** in Finder, press **Cmd+Shift+G** and enter the expected location (not yet verified on a Mac):

```text
~/Library/Application Support/Logi/LogiPluginService/PluginData/HapticAudioFeedback
```

Open `Open Haptic Settings.html` for settings. Diagnostic logs are in the `logs` subfolder, starting with `feel-the-rhythm.log`. The folder and launcher are created when the plugin loads successfully.

## Make it yours

Start with **Music**, **Bass focus**, or **Gentle**. Additional profiles cover electronic, rock, acoustic, cinematic, action, and ambient audio. Profiles tune the response; they do not recognize instruments or gameplay events.

Tuning controls save automatically. Sensitivity changes which sounds trigger feedback; overall haptic intensity is controlled in Logi Options+.

Open **Save & manage your profiles** to duplicate a profile, **Save as new** to keep your current tuning, or **Update selected** to replace a saved custom profile. Profile selection preserves your audio source and paused state; the page shows when you have modified a profile.

The **Toggle haptics**, **Select haptic profile**, and **Preview haptic texture** actions are also available in Options+.

## Troubleshooting

- **No audio:** check the selected source and recording permissions, then use **Refresh** or **Retry audio capture**.
- **Too many or delayed pulses:** increase pulse spacing, try softer textures, and disable sustained texture.
- **Settings conflict:** use **Reload saved settings** to load the latest changes.

Audio is processed locally and is not recorded or uploaded.

## Development and license

See the [developer guide](docs/development.md) for building, testing, CI, and implementation details.

[MIT License](LICENSE) · Copyright 2026 computerfan. Third-party license notices are included in plugin packages.
