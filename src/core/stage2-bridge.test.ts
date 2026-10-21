import { describe, expect, it } from 'vitest';
import {
  createStage2BridgeEnvelope,
  isStage2BridgeEnvelope,
  selectStage2TranslationPort,
  shouldRecreateOffscreenDocument,
  STAGE2_PROTOCOL_VERSION,
} from './stage2-bridge';
import { stage2WorkerReconnectDelay } from './stage2-translation-worker';

describe('Stage 2 bridge contract', () => {
  it('creates a versioned request with a stable request ID', () => {
    const message = createStage2BridgeEnvelope('bridge.health', {}, 'request-1');
    expect(message.protocolVersion).toBe(STAGE2_PROTOCOL_VERSION);
    expect(message.requestId).toBe('request-1');
    expect(isStage2BridgeEnvelope(message)).toBe(true);
  });

  it('rejects unversioned or malformed responses', () => {
    expect(isStage2BridgeEnvelope({ messageType: 'bridge.health.result' })).toBe(false);
    expect(isStage2BridgeEnvelope(null)).toBe(false);
  });

  it('prefers the visible side-panel translator over the offscreen fallback', () => {
    expect(selectStage2TranslationPort('side-panel', 'offscreen')).toBe('side-panel');
    expect(selectStage2TranslationPort(undefined, 'offscreen')).toBe('offscreen');
  });

  it('recreates an orphaned offscreen document that has no live port', () => {
    expect(shouldRecreateOffscreenDocument(true, false)).toBe(true);
    expect(shouldRecreateOffscreenDocument(true, true)).toBe(false);
    expect(shouldRecreateOffscreenDocument(false, false)).toBe(false);
  });

  it('reconnects a live translation page quickly without spinning forever', () => {
    expect(stage2WorkerReconnectDelay(0)).toBe(250);
    expect(stage2WorkerReconnectDelay(1)).toBe(500);
    expect(stage2WorkerReconnectDelay(8)).toBe(2_000);
  });
});
