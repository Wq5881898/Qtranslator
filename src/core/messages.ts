import type { TranslationRequest, TranslationResult } from '../providers/translation-provider';

export type SelectionContext = {
  text: string;
  pageTitle: string;
  pageUrl: string;
  capturedAt: string;
};

export type SelectionTranslation = {
  selection: SelectionContext;
  translation: TranslationResult | null;
  error: string | null;
};

export type CaptureSelectionMessage = {
  type: 'CAPTURE_SELECTION';
  payload: SelectionContext;
};

export type GetLatestTranslationMessage = {
  type: 'GET_LATEST_TRANSLATION';
};

export type TranslationUpdatedMessage = {
  type: 'TRANSLATION_UPDATED';
  payload: SelectionTranslation;
};

export type TranslateMockMessage = {
  type: 'TRANSLATE_MOCK';
  payload: TranslationRequest;
};

export type CheckStage2BridgeMessage = {
  type: 'CHECK_STAGE2_BRIDGE';
};

export type ExtensionMessage =
  | CaptureSelectionMessage
  | GetLatestTranslationMessage
  | TranslationUpdatedMessage
  | TranslateMockMessage
  | CheckStage2BridgeMessage;

export type ExtensionResponse =
  | { ok: true }
  | { ok: true; data: TranslationResult }
  | { ok: true; data: SelectionTranslation | null }
  | { ok: false; error: string };

export function isExtensionMessage(value: unknown): value is ExtensionMessage {
  if (typeof value !== 'object' || value === null || !('type' in value)) {
    return false;
  }

  const { type } = value as { type?: unknown };
  return (
    type === 'CAPTURE_SELECTION' ||
    type === 'GET_LATEST_TRANSLATION' ||
    type === 'TRANSLATION_UPDATED' ||
    type === 'TRANSLATE_MOCK' ||
    type === 'CHECK_STAGE2_BRIDGE'
  );
}
