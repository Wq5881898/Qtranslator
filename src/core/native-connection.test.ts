import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest';
import { NativeBridgeConnection, type NativePort, type BridgeStatus } from './native-connection';
import { createStage2BridgeEnvelope, type Stage2BridgeEnvelope } from './bridge-protocol';

class Port implements NativePort {
  sent: Stage2BridgeEnvelope[] = [];
  message?: (message: unknown) => void;
  disconnected?: () => void;
  onMessage = { addListener: (listener: (message: unknown) => void) => { this.message = listener; } };
  onDisconnect = { addListener: (listener: () => void) => { this.disconnected = listener; } };
  postMessage(message: unknown) { this.sent.push(message as Stage2BridgeEnvelope); }
  disconnect() { this.disconnected?.(); }
  reply(available = true) {
    this.message?.(createStage2BridgeEnvelope('bridge.health.result', { available }, this.sent.at(-1)!.requestId));
  }
}

describe('native connection lifecycle', () => {
  let connection: NativeBridgeConnection;
  let ports: Port[];
  let states: BridgeStatus[];
  let error: string | undefined;
  let lastError: Mock<() => string | undefined>;
  let request: Mock<(port: NativePort, message: unknown) => Promise<void>>;
  beforeEach(() => {
    vi.useFakeTimers(); ports = []; states = []; error = undefined;
    lastError = vi.fn(() => error);
    request = vi.fn(async () => undefined);
    connection = new NativeBridgeConnection({
      connect: () => { const port = new Port(); ports.push(port); return port; },
      lastError, onRequest: request, onStatus: status => states.push(status),
    });
  });
  afterEach(() => { connection.stop(); vi.useRealTimers(); });

  it('requires a correlated successful reply, not merely a port object', async () => {
    connection.start();
    const check = connection.check();
    expect(states.at(-1)?.state).toBe('connecting');
    ports[0]!.message?.(createStage2BridgeEnvelope('bridge.health.result', { available: true }, 'wrong-id'));
    expect(states.at(-1)?.state).toBe('connecting');
    ports[0]!.reply(); await check;
    expect(states.at(-1)?.state).toBe('connected');
  });

  it('consumes Chrome lastError and retains actionable host-not-found details', async () => {
    connection.start();
    const check = connection.check();
    const failure = expect(check).rejects.toThrow('文件资源管理器');
    error = 'Specified native messaging host not found.';
    ports[0]!.disconnected?.(); await failure;
    expect(lastError).toHaveBeenCalled();
    expect(states.at(-1)).toMatchObject({ state: 'disconnected', detail: error });
    await vi.advanceTimersByTimeAsync(2000);
    expect(ports).toHaveLength(2);
    ports[1]!.reply();
    expect(states.at(-1)?.state).toBe('connected');
  });

  it('allows an immediate manual reconnect without duplicate retry timers', async () => {
    connection.start(); ports[0]!.disconnect();
    const check = connection.check();
    ports[1]!.reply(); await check;
    await vi.advanceTimersByTimeAsync(20000);
    expect(ports).toHaveLength(2);
  });

  it('times out unresponsive hosts and rejects negative health responses', async () => {
    connection.start();
    const timeout = expect(connection.check()).rejects.toThrow('未按时响应');
    await vi.advanceTimersByTimeAsync(5000); await timeout;
    const negative = expect(connection.check()).rejects.toThrow();
    ports[1]!.reply(false); await negative;
    expect(states.at(-1)?.state).toBe('disconnected');
  });

  it('keeps desktop requests separate from an in-flight health probe', async () => {
    connection.start(); ports[0]!.reply();
    const check = connection.check();
    const translation = createStage2BridgeEnvelope('translation.request', { text: 'hello' });
    ports[0]!.message?.(translation);
    expect(request).toHaveBeenCalledWith(ports[0]!, translation);
    ports[0]!.reply(); await check;
    expect(states.at(-1)?.state).toBe('connected');
  });

  it('ignores stale port events after a new connection has recovered', async () => {
    connection.start(); ports[0]!.disconnect();
    const check = connection.check(); ports[1]!.reply(); await check;
    ports[0]!.disconnected?.(); ports[0]!.reply();
    expect(states.at(-1)?.state).toBe('connected');
    await vi.advanceTimersByTimeAsync(10000);
    expect(ports).toHaveLength(2);
  });
});
