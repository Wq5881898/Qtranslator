import {
  createStage2BridgeEnvelope, isStage2BridgeEnvelope,
  STAGE2_NATIVE_HOST, STAGE2_PROTOCOL_VERSION,
  STAGE2_VISIBLE_TRANSLATOR_PORT, STAGE2_OFFSCREEN_TRANSLATOR_PORT,
  type Stage2BridgeEnvelope, type Stage2TranslationPayload,
} from './bridge-protocol';
import { NativeBridgeConnection, type NativePort } from './native-connection';
export * from './bridge-protocol';

export const BRIDGE_STATUS_KEY = 'qtranslatorBridgeStatus';
let connection: NativeBridgeConnection | undefined;
function nativeConnection(): NativeBridgeConnection {
  return connection ??= new NativeBridgeConnection({
    connect: () => browser.runtime.connectNative(STAGE2_NATIVE_HOST),
    lastError: () => browser.runtime.lastError?.message,
    onRequest: handleNativeRequest,
    onStatus: status => { void browser.storage.session.set({ [BRIDGE_STATUS_KEY]: status }).catch(() => undefined); },
  });
}

export async function checkStage2NativeHost(): Promise<Stage2BridgeEnvelope> {
  await nativeConnection().check();
  return createStage2BridgeEnvelope('bridge.health.result', { available: true });
}

type RuntimePort = ReturnType<typeof browser.runtime.connect>;

let visibleTranslatorPort: RuntimePort | undefined;
let offscreenTranslatorPort: RuntimePort | undefined;
let translatorPortPromise: Promise<RuntimePort> | undefined;
const pendingTranslationResponses = new Map<
  string,
  {
    port: RuntimePort;
    resolve: (response: Stage2BridgeEnvelope) => void;
    reject: (error: Error) => void;
    timeout: ReturnType<typeof setTimeout>;
  }
>();

export function selectStage2TranslationPort<T>(
  visiblePort: T | undefined,
  offscreenPort: T | undefined,
): T | undefined {
  return visiblePort ?? offscreenPort;
}

export function shouldRecreateOffscreenDocument(
  hasDocument: boolean,
  hasConnectedPort: boolean,
): boolean {
  return hasDocument && !hasConnectedPort;
}

export function registerStage2TranslationPort(port: RuntimePort): boolean {
  if (
    port.name !== STAGE2_VISIBLE_TRANSLATOR_PORT &&
    port.name !== STAGE2_OFFSCREEN_TRANSLATOR_PORT
  ) return false;

  if (port.name === STAGE2_VISIBLE_TRANSLATOR_PORT) {
    visibleTranslatorPort = port;
  } else {
    offscreenTranslatorPort = port;
  }
  port.onMessage.addListener((message: unknown) => {
    if (!isStage2BridgeEnvelope(message)) return;
    const pending = pendingTranslationResponses.get(message.requestId);
    if (!pending || pending.port !== port) return;
    clearTimeout(pending.timeout);
    pendingTranslationResponses.delete(message.requestId);
    pending.resolve(message);
  });
  port.onDisconnect.addListener(() => {
    if (visibleTranslatorPort === port) visibleTranslatorPort = undefined;
    if (offscreenTranslatorPort === port) offscreenTranslatorPort = undefined;
    for (const [requestId, pending] of pendingTranslationResponses) {
      if (pending.port !== port) continue;
      clearTimeout(pending.timeout);
      pending.reject(new Error('The local translation page disconnected. Retry.'));
      pendingTranslationResponses.delete(requestId);
    }
  });
  return true;
}

async function ensureTranslationPort(): Promise<RuntimePort> {
  if (visibleTranslatorPort) return visibleTranslatorPort;
  if (offscreenTranslatorPort) return offscreenTranslatorPort;
  translatorPortPromise ??= (async () => {
    let hasDocument = await browser.offscreen.hasDocument();
    if (shouldRecreateOffscreenDocument(
      hasDocument,
      Boolean(visibleTranslatorPort || offscreenTranslatorPort),
    )) {
      // Chrome can retain an offscreen document after its runtime Port has
      // become detached from a restarted extension worker. Existence alone is
      // therefore not a health check: close the orphan and create a fresh one.
      await browser.offscreen.closeDocument();
      hasDocument = false;
    }
    if (!hasDocument && !visibleTranslatorPort && !offscreenTranslatorPort) {
      await browser.offscreen.createDocument({
        url: 'offscreen.html',
        reasons: ['DOM_SCRAPING'],
        justification: 'Run Chrome local translation in an extension document.',
      });
    }

    const deadline = Date.now() + 5_000;
    while (!visibleTranslatorPort && !offscreenTranslatorPort && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 50));
    }
    const port = selectStage2TranslationPort(visibleTranslatorPort, offscreenTranslatorPort);
    if (!port) {
      throw new Error('The local translation page did not start. Reload the extension.');
    }
    return port;
  })().finally(() => {
    translatorPortPromise = undefined;
  });
  return translatorPortPromise;
}

async function resetOffscreenTranslationPage(): Promise<void> {
  if (visibleTranslatorPort) return;

  const stalePort = offscreenTranslatorPort;
  offscreenTranslatorPort = undefined;
  try {
    stalePort?.disconnect();
  } catch {
    // The stale Port may already have been disconnected by Chrome.
  }

  try {
    if (await browser.offscreen.hasDocument()) {
      await browser.offscreen.closeDocument();
    }
  } catch {
    // A concurrent Chrome cleanup may already have removed the document.
  }
}

async function requestTranslationOnce(
  request: Stage2BridgeEnvelope<Stage2TranslationPayload>,
): Promise<Stage2BridgeEnvelope> {
  const port = await ensureTranslationPort();
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pendingTranslationResponses.delete(request.requestId);
      reject(new Error('Chrome local translation timed out. Retry.'));
    }, 35_000);
    pendingTranslationResponses.set(request.requestId, { port, resolve, reject, timeout });
    port.postMessage(request);
  });
}

async function requestTranslation(
  request: Stage2BridgeEnvelope<Stage2TranslationPayload>,
): Promise<Stage2BridgeEnvelope> {
  try {
    return await requestTranslationOnce(request);
  } catch (firstError) {
    // A sleeping/restarted Chrome worker can leave an orphaned offscreen page
    // or disconnect its Port between the health check and the request. Rebuild
    // once and replay the same idempotent translation request.
    await resetOffscreenTranslationPage();
    try {
      return await requestTranslationOnce(request);
    } catch (retryError) {
      throw new Error(
        'Chrome translation could not recover after rebuilding its local page. Reload the extension or restart Chrome.',
        { cause: retryError ?? firstError },
      );
    }
  }
}

async function handleNativeRequest(
  port: NativePort,
  message: unknown,
): Promise<void> {
  if (!isStage2BridgeEnvelope(message)) {
    port.postMessage(
      createStage2BridgeEnvelope('bridge.error', {
        code: 'invalid_message',
        message: 'The native host sent an invalid message.',
      }),
    );
    return;
  }

  if (message.messageType === 'bridge.health') {
    port.postMessage(
      createStage2BridgeEnvelope(
        'bridge.health.result',
        { available: true, extension: 'qtranslator', protocolVersion: STAGE2_PROTOCOL_VERSION },
        message.requestId,
      ),
    );
    return;
  }

  if (message.messageType !== 'translation.request') {
    port.postMessage(
      createStage2BridgeEnvelope(
        'bridge.error',
        { code: 'unsupported_message', message: `Unsupported message: ${message.messageType}` },
        message.requestId,
      ),
    );
    return;
  }

  try {
    const response = await requestTranslation(
      message as Stage2BridgeEnvelope<Stage2TranslationPayload>,
    );
    port.postMessage(response);
  } catch (error) {
    port.postMessage(
      createStage2BridgeEnvelope(
        'bridge.error',
        {
          code: 'translation_failed',
          message: error instanceof Error ? error.message : 'Chrome local translation failed.',
        },
        message.requestId,
      ),
    );
  }
}

export function startStage2NativeBridge(): void {
  nativeConnection().start();
}
