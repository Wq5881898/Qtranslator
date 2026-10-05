import type { CaptureSelectionMessage } from '../src/core/messages';
import { normalizeSelection } from '../src/core/selection';

export default defineContentScript({
  matches: ['<all_urls>'],
  main() {
    // The companion extension must not compete with an existing Translator panel.
    let automaticCapture = false;
    void browser.storage.local.get('qtranslatorAutomaticWebCapture').then((stored) => {
      automaticCapture = stored.qtranslatorAutomaticWebCapture === true;
    }).catch(() => undefined);
    browser.storage.onChanged.addListener((changes, area) => {
      if (area === 'local' && changes.qtranslatorAutomaticWebCapture) {
        automaticCapture = changes.qtranslatorAutomaticWebCapture.newValue === true;
        previousSelection = '';
      }
    });
    let previousSelection = '';
    let captureTimer: number | undefined;

    function sendCurrentSelection() {
      if (!automaticCapture) return;
      const text = normalizeSelection(window.getSelection()?.toString() ?? '');

      if (!text) {
        previousSelection = '';
        return;
      }

      if (text === previousSelection) {
        return;
      }

      previousSelection = text;

      const message: CaptureSelectionMessage = {
        type: 'CAPTURE_SELECTION',
        payload: {
          text,
          pageTitle: document.title,
          pageUrl: window.location.href,
          capturedAt: new Date().toISOString(),
        },
      };

      void browser.runtime.sendMessage(message).catch(() => {
        // Navigation can disconnect the content script while the message is sent.
      });
    }

    function scheduleSelectionCapture(delay = 0) {
      if (captureTimer !== undefined) {
        window.clearTimeout(captureTimer);
      }

      captureTimer = window.setTimeout(() => {
        captureTimer = undefined;
        sendCurrentSelection();
      }, delay);
    }

    document.addEventListener('selectionchange', () => scheduleSelectionCapture(120));
    document.addEventListener('mouseup', () => scheduleSelectionCapture());
    document.addEventListener('keyup', (event) => {
      if (event.shiftKey) {
        scheduleSelectionCapture();
      }
    });
  },
});
