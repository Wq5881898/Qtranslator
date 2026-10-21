# Qtranslator

Qtranslator is an independent Windows application with Translation and AI Chat tabs, built from the Translator v1.1.8 codebase. It can run alongside the original Translator with separate executables, data, shortcuts, browser extension, and native messaging registration.

- Run `desktop/Qtranslator.exe` from the portable package.
- Screenshot translation: Ctrl+Alt+X. Screenshot-to-chat draft: Ctrl+Shift+D.
- Configure your own API URL, model, key, protocol, system prompt, and token limits in the AI tab.
- Supports OpenAI Chat Completions / Responses and Anthropic Messages, streaming, cancellation, retry, and multi-turn text conversations.
- Settings, favorites, logs and encrypted API configuration use `%LOCALAPPDATA%/Qtranslator/`.
- Load the separate Qtranslator companion extension for Chrome local translation. Keep the original Translator extension installed. Automatic webpage capture is disabled by default.
- Version 1.2.2 checks the live browser connection at startup. Use the desktop connection check and the extension settings' reconnect button to diagnose and recover a disconnected native host.

See [the Chinese setup and isolation guide](docs/QTRANSLATOR_AI_ZH.md) and [validation report](docs/QTRANSLATOR_AI_TEST_REPORT_ZH.md).

Build and validate:

```powershell
npm ci
npm run check
npm run build
npm run verify:release
node scripts/verify-isolation.mjs
dotnet build desktop/Qtranslator.slnx --configuration Release
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --chat
```

Qtranslator 1.2.1 replaces the unisolated 1.2.0 test package. Documentation under `docs/stage-2` describes the upstream project's historical releases; current Qtranslator usage is documented in the guides above.
