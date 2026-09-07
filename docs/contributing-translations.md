# Contribute a language

Feel the Rhythm uses **one XLIFF 1.2 file per language**. It covers Options+ actions, browser settings, profile descriptions, and runtime action-editor labels. JSON files are generated; do not translate them separately.

## Start a translation

1. Download [the English template](localization/HapticAudioFeedback_template.xliff). It contains English sources and empty translation targets. To improve Chinese instead, edit [the existing Chinese XLIFF](../src/package/localization/HapticAudioFeedback_zh-CN.xliff).
2. Save it as `HapticAudioFeedback_<language-ID>.xliff`, for example `HapticAudioFeedback_fr-FR.xliff`, under `src/package/localization/`.
3. Replace **every** `target-language="REPLACE-ME"` with your language ID, such as `fr-FR` or `de-DE`. Keep `source-language="en-US"` unchanged.
4. Translate each `<target>` from its English `<source>`. Remove `state="needs-translation"` when done. An empty target falls back to English; partial translations are welcome.
5. Open a pull request, or [an issue](https://github.com/computerfan/HapticAudioFeedbackPlugin/issues) with the translated file, language name, and any terminology questions. If GitHub rejects the `.xliff` attachment, attach it in a ZIP. Maintainers can run generation and integration checks for translation-only contributions.

```xml
<trans-unit id="example" translate="yes" xml:space="preserve">
  <source>Listening now</source>
  <target>Écoute en cours</target>
</trans-unit>
```

This example illustrates the format: use the template's actual IDs rather than copying `example` into it.

## Translation rules

- Keep IDs, `<source>`, `original`, group names, and file structure unchanged. SDK action IDs must continue to match the plugin.
- Keep placeholders such as `%`, numbers, units, and intentional leading/trailing spaces. Some strings are combined at runtime.
- Use plain text inside targets. Escape XML characters, such as `&amp;` for `&` and `&lt;` for `<`. Do not add HTML or XLIFF inline formatting elements.
- Save as UTF-8. Use a two-letter language and uppercase region (`fr-FR`), matching the filename. The SDK supports languages beyond those built into Options+.
- Translate repeated English sources consistently. Conflicting translations for the same source fail validation.
- Keep labels concise so controls remain readable. Do not translate product names, user-created profile names, or audio device names.

## Generate and test (optional for translators)

From the repository root, with Python 3 installed:

```sh
python tools/generate_localization.py
python tools/generate_localization.py --check
python -m unittest discover -s tests/Localization
node tests/BrowserSettings/device-ui.test.cjs
dotnet run --project tests/BrowserSettings -c Release
```

Use `python3` on macOS if needed. Include generated changes in a code pull request. Generation creates `ui/locales/<language-ID>.json`, updates `ui/locales/available.js`, and refreshes the template. The plugin build also runs generation automatically. No additional translation libraries or online services are required.

After a maintainer builds and installs the package, select the language in Options+ plugin settings and independently in the browser's **Language** menu. Check every tab, profile labels, errors, and narrow windows. Include screenshots if possible. Language changes must not change audio settings.

## When developers add text

Add the English source and stable ID to the canonical Chinese XLIFF (leave its target empty until translated), then regenerate the template. For new SDK actions, export current IDs from the loaded plugin while Logi Plugin Service is running:

```sh
logiplugintool xliff HapticAudioFeedback ./localization.generated
```

Merge new SDK units into the source catalog; do not overwrite the browser/runtime group or existing translations with this export. Regenerate outputs before committing. Existing locale files may be updated from the refreshed template while retaining translations; new strings fall back to English until translated.

[Official Logi SDK localization documentation](https://logitech.github.io/actions-sdk-docs/csharp/plugin-features/plugin-localization/)
