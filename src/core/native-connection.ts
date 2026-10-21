import { createStage2BridgeEnvelope, isStage2BridgeEnvelope } from './bridge-protocol';

export type BridgeStatus = {
  state: 'connecting' | 'connected' | 'disconnected';
  message: string;
  detail?: string;
  updatedAt: string;
};

export interface NativePort {
  postMessage(message: unknown): void;
  disconnect(): void;
  onMessage: { addListener(listener: (message: unknown) => void): void };
  onDisconnect: { addListener(listener: () => void): void };
}

export function explainNativeError(detail: string): string {
  if (/not found/i.test(detail)) return 'Chrome 找不到本机桥接。请从 Windows 文件资源管理器直接打开 Qtranslator.exe，再点击“检查并重连”。仅在开发工具里运行可能不足以完成桌面环境注册。';
  if (/forbidden|access.*denied/i.test(detail)) return 'Chrome 拒绝连接本机桥接。请确认加载的是配套 Qtranslator 插件，并检查浏览器策略。';
  if (/exited|failed to start/i.test(detail)) return '桥接程序未能持续运行。请保留发布包的 desktop、bridge-host 和 extension 文件夹，并重新打开 Qtranslator.exe。';
  if (/timed out/i.test(detail)) return '本机桥接未按时响应。请打开 Qtranslator，并检查是否使用了同一套发布包。';
  return '本机桥接连接中断。将自动重连，也可以打开 Qtranslator 后手动检查。';
}

// A successful connectNative call alone says nothing about host availability.
// Confirm the persistent port with a correlated health response before reporting success.
export class NativeBridgeConnection {
  private port?: NativePort;
  private retry?: ReturnType<typeof setTimeout>;
  private timeout?: ReturnType<typeof setTimeout>;
  private attempts = 0;
  private pending?: { id: string; resolve: () => void; reject: (error: Error) => void; promise: Promise<void> };

  constructor(private readonly adapter: {
    connect: () => NativePort;
    lastError: () => string | undefined;
    onRequest: (port: NativePort, message: unknown) => Promise<void>;
    onStatus: (status: BridgeStatus) => void;
  }) {}

  start(): void {
    if (this.port) return;
    if (this.retry) clearTimeout(this.retry);
    this.retry = undefined;
    this.update('connecting', '正在连接本机桥接…');
    try {
      const port = this.adapter.connect();
      this.port = port;
      port.onDisconnect.addListener(() => {
        // Read lastError inside the callback, including for an obsolete port.
        const error = this.adapter.lastError() ?? 'Native host disconnected.';
        if (this.port === port) this.fail(port, error);
      });
      port.onMessage.addListener(message => {
        if (this.port !== port) return;
        if (isStage2BridgeEnvelope(message) && message.requestId === this.pending?.id) {
          if (message.messageType !== 'bridge.health.result' ||
              (message.payload as { available?: unknown }).available !== true) {
            this.fail(port, 'Invalid health response from native host.');
            return;
          }
          this.finishProbe();
          this.attempts = 0;
          this.update('connected', '浏览器与本机桥接已连接。请在桌面端检查完整翻译连接。');
          return;
        }
        void this.adapter.onRequest(port, message).catch(() => {
          if (this.port === port) this.fail(port, 'Native host disconnected while responding.');
        });
      });
      void this.probe().catch(() => undefined);
    } catch (error) {
      this.fail(this.port, error instanceof Error ? error.message : String(error));
    }
  }

  async check(): Promise<void> {
    this.start();
    if (this.pending) return this.pending.promise;
    if (!this.port) throw new Error('本机桥接无法启动，请查看下方连接详情。');
    return this.probe();
  }

  private probe(): Promise<void> {
    if (this.pending) return this.pending.promise;
    const port = this.port;
    if (!port) return Promise.reject(new Error('Native host is not connected.'));
    const request = createStage2BridgeEnvelope('bridge.health', {});
    let resolve!: () => void;
    let reject!: (error: Error) => void;
    const promise = new Promise<void>((yes, no) => { resolve = yes; reject = no; });
    this.pending = { id: request.requestId, resolve, reject, promise };
    this.timeout = setTimeout(() => this.fail(port, 'Native host health check timed out.'), 5_000);
    try { port.postMessage(request); }
    catch (error) { this.fail(port, error instanceof Error ? error.message : String(error)); }
    return promise;
  }

  private finishProbe(error?: string): void {
    if (this.timeout) clearTimeout(this.timeout);
    this.timeout = undefined;
    const pending = this.pending;
    this.pending = undefined;
    if (error) pending?.reject(new Error(explainNativeError(error)));
    else pending?.resolve();
  }

  private fail(port: NativePort | undefined, detail: string): void {
    if (port !== this.port) return;
    this.port = undefined;
    this.finishProbe(detail);
    try { port?.disconnect(); } catch { /* Already closed by Chrome. */ }
    this.update('disconnected', explainNativeError(detail), detail);
    if (this.retry) clearTimeout(this.retry);
    this.retry = setTimeout(() => this.start(), Math.min(2_000 * 2 ** Math.min(this.attempts++, 3), 10_000));
  }

  private update(state: BridgeStatus['state'], message: string, detail?: string): void {
    this.adapter.onStatus({ state, message, detail, updatedAt: new Date().toISOString() });
  }

  stop(): void {
    if (this.retry) clearTimeout(this.retry);
    this.retry = undefined;
    const port = this.port;
    this.port = undefined;
    this.finishProbe('Native connection stopped.');
    try { port?.disconnect(); } catch { /* Already disconnected. */ }
  }
}
