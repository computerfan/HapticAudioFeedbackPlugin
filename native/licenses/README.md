# Supplemental license sources

The hash directories contain license declarations/text from the exact Git revision in each locked crate's `.cargo_vcs_info.json`; `SOURCE.txt` records that URL. These fill omissions in the crates.io archives.

`objc2-LICENSE-MIT.txt` is the upstream MIT license text retrieved from https://raw.githubusercontent.com/madsmtm/objc2/main/LICENSE-MIT.txt on 2026-09-06. The exact older crate declarations select MIT but link the standard license without shipping its text. Generated notices also retain Cargo package authors and the older declaration, including its Apple SDK caveat. This text supplements that declaration; it does not replace it or relicense third-party code.

The crate graph audit covers linked library dependencies. Compiler/procedural-macro/build-only code is not shipped in the plugin. Recheck the resolved graph and notices whenever Cargo.lock changes.
