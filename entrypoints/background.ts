import {
  isExtensionMessage,
  type CaptureSelectionMessage,
  type ExtensionResponse,
  type SelectionTranslation,
  type TranslationUpdatedMessage,
} from '../src/core/messages';
import { normalizeSelection } from '../src/core/selection';
import { mockTranslationProvider } from '../src/providers/mock-translation-provider';
import {
  checkStage2NativeHost,
  registerStage2TranslationPort,
  startStage2NativeBridge,
} from '../src/core/stage2-bridge';

const LATEST_TRANSLATION_KEY = 'latestSelectionTranslation';

async function publishState(state: SelectionTranslation): Promise<void> {
  await browser.storage.session.set({
    [LATEST_TRANSLATION_KEY]: state,
  });

  const update: TranslationUpdatedMessage = {
    type: 'TRANSLATION_UPDATED',
    payload: state,
  };

  await browser.runtime.sendMessage(update).catch(() => undefined);
}

async function captureSelection(
  message: CaptureSelectionMessage,
): Promise<SelectionTranslation> {
  const text = normalizeSelection(message.payload.text);
  const state: SelectionTranslation = {
    selection: {
      ...message.payload,
      text,
    },
    translation: null,
    error: text ? null : 'Select some English text first.',
  };

  await publishState(state);
  return state;
}

export default defineBackground(() => {
  browser.runtime.onConnect.addListener((port) => {
    registerStage2TranslationPort(port);
  });
  startStage2NativeBridge();
  browser.runtime.onStartup.addListener(startStage2NativeBridge);

  browser.runtime.onInstalled.addListener(() => {
    browser.contextMenus.create({
      id: 'translator-open-side-panel',
      title: 'Qtranslator: Translate selection',
      contexts: ['selection'],
    });

    void browser.sidePanel.setPanelBehavior({
      openPanelOnActionClick: true,
    });
  });

  browser.contextMenus.onClicked.addListener((info, tab) => {
    if (info.menuItemId !== 'translator-open-side-panel') {
      return;
    }

    const text = normalizeSelection(info.selectionText ?? '');

    if (text) {
      void captureSelection({
        type: 'CAPTURE_SELECTION',
        payload: {
          text,
          pageTitle: tab?.title ?? '',
          pageUrl: tab?.url ?? '',
          capturedAt: new Date().toISOString(),
        },
      });
    }

    if (tab?.windowId !== undefined) {
      void browser.sidePanel.open({ windowId: tab.windowId });
    }
  });

  browser.runtime.onMessage.addListener(
    async (message: unknown, sender): Promise<ExtensionResponse> => {
      if (!isExtensionMessage(message)) {
        return { ok: false, error: 'Unsupported message.' };
      }

      if (message.type === 'TRANSLATION_UPDATED') {
        return { ok: true };
      }

      if (message.type === 'GET_LATEST_TRANSLATION') {
        const stored = await browser.storage.session.get(LATEST_TRANSLATION_KEY);
        const data = stored[LATEST_TRANSLATION_KEY] as SelectionTranslation | undefined;
        return { ok: true, data: data ?? null };
      }

      if (message.type === 'CAPTURE_SELECTION') {
        const data = await captureSelection(message);

        if (sender.tab?.windowId !== undefined) {
          await browser.sidePanel
            .open({ windowId: sender.tab.windowId })
            .catch(() => undefined);
        }

        return { ok: true, data };
      }

      if (message.type === 'CHECK_STAGE2_BRIDGE') {
        try {
          const data = await checkStage2NativeHost();
          return { ok: true, data } as ExtensionResponse;
        } catch (error) {
          return {
            ok: false,
            error: error instanceof Error ? error.message : 'Stage 2 bridge check failed.',
          };
        }
      }

      try {
        const data = await mockTranslationProvider.translate(message.payload);
        return { ok: true, data };
      } catch (error) {
        return {
          ok: false,
          error: error instanceof Error ? error.message : 'Translation failed.',
        };
      }
    },
  );
});
