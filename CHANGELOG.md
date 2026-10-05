# Changelog

## Qtranslator 1.2.1 — independent application

- Rename Windows projects, assemblies, namespaces and executable to Qtranslator.
- Isolate configuration, favorites, logs, native host registration, named pipe, installer AppId, and extension ID from the legacy Translator product.
- Use Ctrl+Alt+X for new screenshot translation, reserve Ctrl+Shift+X for legacy Translator, and retain Ctrl+Shift+D for AI drafts.
- Disable automatic webpage selection capture by default in the independent companion extension; it can be enabled explicitly.
- Retire legacy installer workflows and replace the unisolated 1.2.0 test package.

## Qtranslator desktop 1.2.0

- Upgrade the Windows desktop base to Translator v1.1.8 and add Translation / AI Chat tabs.
- Add configurable OpenAI Chat Completions, OpenAI Responses, and Anthropic Messages APIs with editable profiles, URL, model, key, auth header, system prompt, and token limits.
- Add streaming conversations, cancellation, explicit retry, copy, and new conversation controls.
- Reuse screenshot OCR with Ctrl+Shift+D to draft AI prompts, retaining bilingual text and the existing translation shortcut.
- Protect AI configuration with Windows-user DPAPI and validate protocol behavior with simulated services.

## 1.1.8 — 2026-08-27

- Convert Datamuse/CMU ARPAbet pronunciation codes to validated learner-facing IPA and reject unparseable codes.
- Bind each part of speech to its corresponding translated dictionary meanings instead of showing a detached label.
- Display the actual installed version in both the Chrome extension and Windows application.
- Add regression coverage for `changes` (`CH EY1 N JH AH0 Z` → `/ˈtʃeɪndʒəz/`) and noun/verb meaning grouping.

## 1.1.5 — 2026-08-25

- Rebuilds an orphaned Chrome offscreen translation page when its runtime port has disappeared.
- Retries one idempotent translation request after rebuilding the browser translation surface.
- Falls back to basic Chrome local translation when the optional online dictionary is unavailable.
- Replaces the misleading language-pack-only waiting message with an accurate multi-stage status.

## 1.1.4 — 2026-08-24

- Keep repeated manual word translations deterministic by caching a completed dictionary lookup in each translation worker.
- Retry transient dictionary connections three times inside one user action; persistent failures now report an error instead of silently returning a different partial result.
- Prevent Bridge health-check exceptions from escaping WPF event handlers and terminating the Windows application.
- Add a final dispatcher exception guard and `%LOCALAPPDATA%\\Translator\\logs\\desktop.log` diagnostics.

## 1.1.3 — 2026-08-21

- Normalize IPA combining tie bars so affricates render as familiar learner-dictionary forms such as `crouch /kraʊtʃ/` and `judge /dʒʌdʒ/` instead of displaying detached arcs.

## 1.1.2 — 2026-08-21

- Fixed a word-lookup race where the first OCR translation could omit phonetics and dictionary senses until Translate Chinese was clicked twice.
- Retry one transient dictionary connection failure before falling back to basic local translation.
- Keep a valid original dictionary entry instead of replacing words such as `during` with an unrelated guessed lemma.
- Prefer explicitly tagged US pronunciation data and normalize IPA for a consistent learner-facing display (`/praʊd/`, `/ˈdjʊərɪŋ/`).

## 1.1.1 — 2026-08-21

### Fixed

- normalized syllabic-consonant IPA so `consultation` renders as `/ˌkɒnsəlˈteɪʃən/` without missing glyphs;
- rejected corrupted dictionary phonetics and preserved UTF-8 IPA through the native bridge;
- serialized Chrome Translator work and recreated failed sessions so one word cannot poison later translations;
- routed Windows translations through a working visible side-panel session before the offscreen fallback;
- corrected the local-language-pack recovery instruction;
- made per-user installation robust when the Windows shell-folder API is unavailable;
- prevented Chrome from restarting Bridge Host during an in-place upgrade and locking runtime files.

### Verified

- 46 extension tests, release manifest verification, and 11 Windows technical checks;
- self-contained x64 publish, installer compilation, real install, locked-host in-place upgrade, startup, registry, and favorites-preservation checks.

## 1.0.0-rc.1 — 2026-07-28

First-stage release candidate.

### Added

- automatic webpage-selection translation;
- right-click translation fallback;
- Chrome local English-to-Chinese translation;
- optional Azure fallback;
- free word phonetics;
- local English pronunciation with US/UK preference;
- local word and sentence favorites;
- hidden favorites window with heart-state controls;
- Excel-friendly CSV favorites import and export;
- two-step local-data clearing;
- privacy, permission, installation, and testing documentation;
- release artifact permission verification.

### Fixed

- removed visible test output after formal translation starts;
- restored automatic translation after repeated selections;
- restored automatic selection behavior across refreshed tabs;
- stopped prior pronunciation when translation or playback changes;
- closed settings after a successful save;
- prevented duplicate imported favorites;
- rejected malformed imports without partial writes;
- added understandable errors for unavailable providers, invalid input, oversized text, timeouts, and storage failures.

### Deferred

- translation-count statistics;
- Windows screenshot OCR;
- browser-store publication.
