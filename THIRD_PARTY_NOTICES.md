# Third-Party Notices

Translator uses open-source software. The repository's installed dependency tree and lockfile are the authoritative version record.

## Runtime and build dependencies

- React and React DOM — Meta Platforms, Inc. and contributors — MIT License.
- WXT and `@wxt-dev/module-react` — WXT contributors — MIT License.
- TypeScript — Microsoft Corporation — Apache License 2.0.
- Vitest — Vitest contributors — MIT License.

Additional desktop OCR dependencies:

- Tesseract .NET wrapper 5.2.0 — Charles Weld and contributors — Apache License 2.0.
- Tesseract OCR engine — Tesseract contributors — Apache License 2.0.
- `tessdata_fast/eng.traineddata` and `chi_sim.traineddata` — Tesseract contributors — Apache License 2.0.

The packaged English model is sourced from
`https://github.com/tesseract-ocr/tessdata_fast/blob/main/eng.traineddata`.
Its SHA-256 is
`7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`.
The Simplified Chinese model is sourced from
`https://github.com/tesseract-ocr/tessdata_fast/blob/main/chi_sim.traineddata`.
Its SHA-256 is
`A5FCB6F0DB1E1D6D8522F39DB4E848F05984669172E584E8D76B6B3141E1F730`.

These packages include transitive dependencies under their respective licenses. Their license texts and package metadata are distributed through the installed packages.

## External platform services

The following are services or browser capabilities, not bundled libraries:

- Chrome built-in Translator API — local browser capability.
- Web Speech API / operating-system speech synthesis — local browser or OS capability.
- Free Dictionary API (`api.dictionaryapi.dev`) — optional word-phonetic lookup.
- Microsoft Azure Translator — optional user-configured fallback.

No third-party code or service is presented as owned by Translator.
