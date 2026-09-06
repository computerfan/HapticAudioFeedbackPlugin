# Feel the Rhythm logo and action icons

Selected direction: C — Moving beat. Three ascending pulses share a wave-shaped cut.

- `feel-the-rhythm-master.png`: original refined source from the built-in image generation tool.
- `feel-the-rhythm-plugin-master.png`: transparent badge variant from the same tool.
- `../../src/package/metadata/Icon256x256.png`: 256 × 256 RGBA plugin icon. All visible pixels fit within the centered 192 × 192 safe area; surrounding pixels are transparent.
- `../../src/package/ui/logo.png`: original opaque export for the browser header and favicon.

The plugin master is downsampled with bicubic interpolation into a 224 × 224 rectangle at (16, 16) on a transparent 256 × 256 canvas. Its generated transparent margins provide the remaining inset. The wordmark uses the host UI's system font; no font files are bundled.

## Action assets

Both `src/package/actionicons/` (assigned actions) and `src/package/actionsymbols/` (action picker) contain these original, hand-authored SVGs:

| Action class | Symbol |
| --- | --- |
| `ConfigureAudioHaptics` | Sliders |
| `ToggleAudioHaptics` | Power |
| `SelectAudioProfile` | Equalizer |
| `PreviewAudioHaptic` | Simplified Moving beat mark |

Every filename starts with the unchanged namespace `Loupedeck.HapticAudioFeedback.`. The SVGs have a 64 × 64 viewBox, transparent backgrounds, and black strokes/fills. They use only inline geometry, with no fonts, scripts, embedded bitmaps, or external dependencies. The same artwork is copied to both folders. They are covered by the repository's MIT license.

The 64-unit canvas is our design choice, not an SDK size requirement. Static assets do not indicate the live enabled/disabled state. Logi Plugin Service must be restarted after adding them. User-customized icons may need to be reset to default in Options+.

SDK references: [Action symbols](https://logitech.github.io/actions-sdk-docs/csharp/icons/action-symbols/), [Action images](https://logitech.github.io/actions-sdk-docs/csharp/icons/vector-images/), [Plugin icon](https://logitech.github.io/actions-sdk-docs/csharp/icons/plugin-icon/), [Icon templates and user overrides](https://logitech.github.io/actions-sdk-docs/csharp/icons/icon-templates/).

## Transparent plugin master generation prompt

Built-in image generation edit, using `src/package/ui/logo.png` as the reference:

> Edit target: the provided Feel the Rhythm app icon. Prepare a transparent-background plugin icon preserving exactly the recognizable three ascending right-leaning mint pulse columns with a continuous wave cut. Keep the dark charcoal green background only as a rounded-square badge behind the mark; the badge should occupy the centered 74% of the square canvas, with completely transparent margins outside it. Preserve the mint mark inside the badge at the same proportions as the reference, without redesigning its shapes. Uniform flat dark green badge fill and flat pale mint mark. Output actual RGBA transparency, not a checkerboard drawing. No shadows, no glow, no text, no decorations. Square output. The complete badge including antialiasing must stay in the central 75% of the canvas.

The generated source needed the export inset described above to satisfy the pixel-level safe-area check.
