# Qtranslator Privacy Policy

Last updated: 2026-10-05

Qtranslator includes an independent Windows application and companion browser extension.

## Qtranslator desktop AI chat

The optional desktop AI Chat tab sends manually confirmed messages, the system prompt, and successful turns in the current conversation directly to the configured API endpoint. The configured API key is sent in the selected authentication header. Changing the selected provider sends the current conversation's successful history to that provider when the next message is sent. No automatic fallback provider is used.

Ctrl+Shift+D captures a screen region for local OCR and places recognized text in an editable draft. Screenshots are not uploaded. Drafts are sent only after the user chooses Send. Chat history stays in memory and is discarded when the application closes; Qtranslator favorites use their independent local store at `%LOCALAPPDATA%/Qtranslator/favorites.json`. The legacy Translator store is not read, migrated or modified.

AI settings and keys are encrypted for the current Windows user using DPAPI at `%LOCALAPPDATA%/Qtranslator/ai-settings.bin`. Keys are not included in source code, packages, or application logs. To remove AI configuration, close the application and delete that file. Browser extension data clearing does not delete desktop AI settings. The selected third-party service applies its own data-retention policy; the Responses request sets `store: false` where supported.

## Data processed locally

Translator stores the following in Chrome or Edge extension-local storage:

- favorite words and sentences;
- phonetics, Chinese translations, and first-saved times associated with favorites;
- pronunciation preference;
- optional Azure fallback configuration.

Local pronunciation uses the browser or operating-system speech engine. CSV import and export use files explicitly selected or downloaded by the user.

## Data sent outside the browser

Chrome local translation is the default and requires no Translator-operated server.

When Azure fallback is explicitly enabled and local translation fails, Translator sends only the selected English text to Microsoft Azure Translator. The Azure key and region are sent to Azure to authorize that request.

For single-word phonetics, Translator may send only that word to `api.dictionaryapi.dev`.

## Data not collected or uploaded

Translator does not collect or upload:

- favorites or settings;
- browsing history;
- complete webpage HTML;
- screenshots;
- audio recordings;
- analytics, advertising identifiers, or telemetry.

Translator does not operate a backend server.

## API key storage

The optional Azure key is stored in extension-local storage and is not embedded in source code. Translator does not add its own encryption layer. Users who do not need Azure should leave fallback disabled and the key blank.

## Permissions

- `storage`: saves settings and favorites locally.
- `contextMenus`: provides a manual translation fallback.
- `sidePanel`: displays translations and favorites.
- webpage content-script access: detects text selected on webpages for automatic translation.
- Azure and Dictionary API host access: used only for the optional fallback and word phonetics described above.

## Control and deletion

Users can export favorites as CSV. The settings page provides a two-step **Clear local data** action that removes Translator settings and favorites from the current browser profile. Uninstalling the extension also removes its extension-local storage according to browser behavior.

## Stage 1 scope

Translation-count statistics and screenshot OCR are not included in this release candidate.
