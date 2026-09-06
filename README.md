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

## Get started

1. Install a plugin package built for your platform. To build one, see the [developer guide](docs/development.md).
2. In Logi Options+, assign **Open haptic settings** to an Actions Ring slot and activate it.
3. Choose an **Audio source** and press **Use device**. Select a **Starting profile**, press **Apply profile**, and play some audio.

You can also open **Open Haptic Settings.html** from the plugin's data folder. Reopen this launcher after a plugin restart instead of bookmarking the temporary browser address.

## Make it yours

Start with **Music**, **Bass focus**, or **Gentle**. Additional profiles cover electronic, rock, acoustic, cinematic, action, and ambient audio. Profiles tune the response; they do not recognize instruments or gameplay events.

Tuning controls save automatically. Sensitivity changes which sounds trigger feedback; overall haptic intensity is controlled in Logi Options+.

Use **Duplicate selected** to copy a profile, **Save current as new** to keep your current tuning, or **Update selected custom** to replace a saved custom profile. Applying a profile preserves your audio source and paused state.

The **Toggle haptics**, **Select haptic profile**, and **Preview haptic texture** actions are also available in Options+.

## Troubleshooting

- **No audio:** check the selected source and recording permissions, then use **Refresh devices** or **Retry audio capture**.
- **Too many or delayed pulses:** increase pulse spacing, try softer textures, and disable sustained texture.
- **Settings conflict:** use **Reload saved settings** to load the latest changes.

Audio is processed locally and is not recorded or uploaded.

## Development and license

See the [developer guide](docs/development.md) for building, testing, CI, and implementation details.

[MIT License](LICENSE) · Copyright 2026 computerfan. Third-party license notices are included in plugin packages.
