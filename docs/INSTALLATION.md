# Installation and Update Guide

## Stage 2 Windows final (v1.1.8)

1. Run `Translator-Setup-v1.1.8.exe`. It installs per user and does not require PowerShell or manual Bridge registry editing.
2. Open `chrome://extensions`, enable Developer mode, choose **Load unpacked**, and select `%LOCALAPPDATA%\Programs\Translator\extension`.
3. On upgrade, run the new installer, then reload Translator on `chrome://extensions` or fully restart Chrome.
4. Start Translator from the desktop shortcut. Use `Ctrl+Alt+A` or **Select screen region** to capture, recognize, and translate.

The installer registers the Native Messaging Bridge automatically. Favorites remain under `%LOCALAPPDATA%\Translator`; diagnostic logs are written to `%LOCALAPPDATA%\Translator\logs\desktop.log`. The production installer does not contain `Translator.TechnicalValidation.exe`, which is only a source/CI tool.

## Chrome developer installation

1. Download and extract the release-candidate ZIP.
2. Open `chrome://extensions`.
3. Enable **Developer mode**.
4. Click **Load unpacked**.
5. Select the extracted directory containing `manifest.json`.
6. Pin Translator if desired and open its side panel.
7. Refresh webpages that were already open before installation.

## Edge developer installation

1. Download and extract the same release-candidate ZIP.
2. Open `edge://extensions`.
3. Enable **Developer mode**.
4. Click **Load unpacked**.
5. Select the extracted directory containing `manifest.json`.
6. Refresh webpages that were already open.

The extension uses Manifest V3 and the same Chromium build for Chrome and Edge. Chrome local Translator API availability can differ between browser versions. When unavailable, Translator shows a recoverable message; Azure remains an optional user-configured fallback.

## Updating an unpacked build

1. Extract the new build to a new directory or replace the old extracted files.
2. Open the browser extensions page.
3. Click the reload icon on Translator.
4. Refresh every already-open webpage used for selection translation.

## Removing the extension

Use **Remove** from the browser extensions page. To explicitly clear Translator data before removal, open extension options and use the two-step **Clear local data** action.
