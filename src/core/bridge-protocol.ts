export const STAGE2_NATIVE_HOST = 'com.wq5881898.qtranslator';
export const STAGE2_PROTOCOL_VERSION = '1.0';
export const STAGE2_MAX_TEXT_LENGTH = 5_000;
export const STAGE2_VISIBLE_TRANSLATOR_PORT = 'stage2-translator-visible';
export const STAGE2_OFFSCREEN_TRANSLATOR_PORT = 'stage2-translator-offscreen';

export type Stage2BridgeEnvelope<T = unknown> = {
  protocolVersion: typeof STAGE2_PROTOCOL_VERSION;
  messageType: string;
  requestId: string;
  sentAt: string;
  payload: T;
};

export type Stage2TranslationPayload = {
  text: string;
  sourceLanguage: 'en';
  targetLanguage: 'zh-CN';
};

export function createStage2BridgeEnvelope<T>(
  messageType: string,
  payload: T,
  requestId: string = crypto.randomUUID(),
): Stage2BridgeEnvelope<T> {
  return {
    protocolVersion: STAGE2_PROTOCOL_VERSION,
    messageType,
    requestId,
    sentAt: new Date().toISOString(),
    payload,
  };
}

export function isStage2BridgeEnvelope(value: unknown): value is Stage2BridgeEnvelope {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<Stage2BridgeEnvelope>;
  return (
    candidate.protocolVersion === STAGE2_PROTOCOL_VERSION &&
    typeof candidate.messageType === 'string' &&
    typeof candidate.requestId === 'string' &&
    typeof candidate.sentAt === 'string' &&
    typeof candidate.payload === 'object' &&
    candidate.payload !== null
  );
}

