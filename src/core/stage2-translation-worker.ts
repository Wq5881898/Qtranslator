import { chromeTranslationProvider } from '../providers/chrome-translation-provider';
import {
  createStage2BridgeEnvelope,
  isStage2BridgeEnvelope,
  STAGE2_OFFSCREEN_TRANSLATOR_PORT,
  STAGE2_VISIBLE_TRANSLATOR_PORT,
  type Stage2BridgeEnvelope,
  type Stage2TranslationPayload,
} from './stage2-bridge';

export { STAGE2_OFFSCREEN_TRANSLATOR_PORT, STAGE2_VISIBLE_TRANSLATOR_PORT };

let startedPortName: string | undefined;
let activePort: ReturnType<typeof browser.runtime.connect> | undefined;
let reconnectTimer: ReturnType<typeof setTimeout> | undefined;
let reconnectAttempt = 0;

export function stage2WorkerReconnectDelay(attempt: number): number {
  return Math.min(250 * 2 ** Math.max(0, attempt), 2_000);
}

export function startStage2TranslationWorker(portName: string): void {
  if (startedPortName === portName && activePort) return;
  startedPortName = portName;

  const connect = () => {
    if (activePort || startedPortName !== portName) return;

    let port: ReturnType<typeof browser.runtime.connect>;
    try {
      port = browser.runtime.connect({ name: portName });
    } catch {
      scheduleReconnect();
      return;
    }
    activePort = port;
    reconnectAttempt = 0;

  port.onMessage.addListener((message: unknown) => {
    void (async () => {
      if (!isStage2BridgeEnvelope(message) || message.messageType !== 'translation.request') {
        return;
      }

      const request = message as Stage2BridgeEnvelope<Stage2TranslationPayload>;
      try {
        const result = await chromeTranslationProvider.translate({
          text: request.payload.text,
          sourceLanguage: 'en',
          targetLanguage: 'zh-CN',
        });
        try {
          port.postMessage(
            createStage2BridgeEnvelope('translation.result', result, request.requestId),
          );
        } catch {
          // The background worker will replay the idempotent request after the
          // replacement port connects.
        }
      } catch (error) {
        try {
          port.postMessage(
            createStage2BridgeEnvelope(
              'bridge.error',
              {
                code: 'translation_failed',
                message: error instanceof Error
                  ? error.message
                  : 'Chrome local translation failed.',
              },
              request.requestId,
            ),
          );
        } catch {
          // The disconnected request is rejected and replayed by the bridge.
        }
      }
    })();
  });

    port.onDisconnect.addListener(() => {
      if (activePort === port) activePort = undefined;
      scheduleReconnect();
    });
  };

  const scheduleReconnect = () => {
    if (reconnectTimer || startedPortName !== portName) return;
    const delay = stage2WorkerReconnectDelay(reconnectAttempt++);
    reconnectTimer = setTimeout(() => {
      reconnectTimer = undefined;
      connect();
    }, delay);
  };

  connect();
}
