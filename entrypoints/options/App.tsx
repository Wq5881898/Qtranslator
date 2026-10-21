import { type FormEvent, useEffect, useState } from 'react';

import { FAVORITES_STORAGE_KEY } from '../../src/core/favorites';
import { FAVORITES_SYNC_METADATA_KEY } from '../../src/core/favorites-sync';
import { BRIDGE_STATUS_KEY } from '../../src/core/stage2-bridge';
import type { BridgeStatus } from '../../src/core/native-connection';
import {
  normalizeSettings,
  TRANSLATOR_SETTINGS_KEY,
  type TranslatorSettings,
} from '../../src/core/settings';

const EMPTY_SETTINGS: TranslatorSettings = {
  pronunciationLanguage: 'en-US',
  azureFallbackEnabled: false,
  azureKey: '',
  azureRegion: '',
};

export function App() {
  const [settings, setSettings] = useState(EMPTY_SETTINGS);
  const [automaticCapture, setAutomaticCapture] = useState(false);
  const [clearArmed, setClearArmed] = useState(false);
  const [bridgeStatus, setBridgeStatus] = useState<BridgeStatus>();
  const [checkingBridge, setCheckingBridge] = useState(false);
  const [bridgeCheckError, setBridgeCheckError] = useState('');
  const [status, setStatus] = useState(
    'US English pronunciation is active. Chrome local translation is the default.',
  );

  useEffect(() => {
    let active = true;
    let changed = false;
    const listener = (changes: Record<string, { newValue?: unknown }>, area: string) => {
      if (area === 'session' && changes[BRIDGE_STATUS_KEY]) {
        changed = true;
        if (active) setBridgeStatus(changes[BRIDGE_STATUS_KEY].newValue as BridgeStatus);
      }
    };
    browser.storage.onChanged.addListener(listener);
    void browser.storage.session.get(BRIDGE_STATUS_KEY).then(stored => {
      if (active && !changed) setBridgeStatus(stored[BRIDGE_STATUS_KEY] as BridgeStatus | undefined);
    }).catch(() => undefined);
    return () => { active = false; browser.storage.onChanged.removeListener(listener); };
  }, []);

  async function checkBridge() {
    setCheckingBridge(true);
    setBridgeCheckError('');
    try {
      const response = await browser.runtime.sendMessage({ type: 'CHECK_STAGE2_BRIDGE' });
      if (!response?.ok) setBridgeCheckError(response?.error ?? '连接检查未返回结果，请重新加载插件。');
    } catch (error) {
      setBridgeCheckError(error instanceof Error ? error.message : '连接检查失败。');
    } finally { setCheckingBridge(false); }
  }

  useEffect(() => {
    let active = true;

    void browser.storage.local
      .get([TRANSLATOR_SETTINGS_KEY, 'qtranslatorAutomaticWebCapture'])
      .then((stored) => {
        if (!active) {
          return;
        }
        setAutomaticCapture(stored.qtranslatorAutomaticWebCapture === true);

        const saved = stored[TRANSLATOR_SETTINGS_KEY] as
          | Partial<TranslatorSettings>
          | undefined;
        if (saved) {
          const normalized = normalizeSettings(saved);
          setSettings(normalized);
          setStatus(
            `${normalized.pronunciationLanguage === 'en-GB' ? 'UK' : 'US'} English pronunciation loaded locally`,
          );
        }
      })
      .catch(() => {
        if (active) {
          setStatus('Settings could not be read. Reload this page and try again.');
        }
      });

    return () => {
      active = false;
    };
  }, []);

  async function saveSettings(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const normalized = normalizeSettings(settings);

    if (
      normalized.azureFallbackEnabled &&
      (!normalized.azureKey || !normalized.azureRegion)
    ) {
      setStatus('Enter both the Azure key and region, or turn the fallback off');
      return;
    }

    try {
      await browser.storage.local.set({
        [TRANSLATOR_SETTINGS_KEY]: normalized,
        qtranslatorAutomaticWebCapture: automaticCapture,
      });
      setSettings(normalized);
      setStatus(
        `${normalized.pronunciationLanguage === 'en-GB' ? 'UK' : 'US'} English pronunciation saved locally. Closing…`,
      );
      window.setTimeout(() => window.close(), 700);
    } catch {
      setStatus('Settings could not be saved. Check Chrome storage and try again.');
    }
  }

  async function clearLocalData() {
    if (!clearArmed) {
      setClearArmed(true);
      setStatus('Click “Confirm clear local data” to permanently remove settings and favorites.');
      return;
    }

    try {
      await browser.storage.local.remove([
        TRANSLATOR_SETTINGS_KEY,
        FAVORITES_STORAGE_KEY,
        FAVORITES_SYNC_METADATA_KEY,
        'qtranslatorAutomaticWebCapture',
      ]);
      setSettings(EMPTY_SETTINGS);
      setAutomaticCapture(false);
      setClearArmed(false);
      setStatus('Local settings and favorites were cleared.');
    } catch {
      setStatus('Local data could not be cleared. Reload this page and try again.');
    }
  }

  return (
    <main>
      <p className="eyebrow">Version {browser.runtime.getManifest().version}</p>
      <h1>Qtranslator settings</h1>
      <p className="intro">
        Pronunciation, settings, and favorites stay in this browser. Translation runs locally in Chrome by default.
      </p>

      <section className="privacy-card" aria-labelledby="bridge-heading">
        <h2 id="bridge-heading">桌面连接</h2>
        <p role="status">{bridgeStatus?.message ?? '尚未检查本机桥接。请先从 Windows 文件资源管理器打开 Qtranslator.exe。'}</p>
        {bridgeStatus && <p>最近检查：{new Date(bridgeStatus.updatedAt).toLocaleString()}</p>}
        {bridgeStatus?.detail && <details><summary>Chrome 错误详情</summary><p>{bridgeStatus.detail}</p></details>}
        <button type="button" disabled={checkingBridge} onClick={() => void checkBridge()}>
          {checkingBridge ? '正在检查…' : '检查并重连'}
        </button>
        {bridgeCheckError && <p role="alert">{bridgeCheckError}</p>}
        <p>此处检查浏览器与桥接程序的连接。桌面程序中的“检查连接”会验证完整路径。AI 对话不依赖此插件。</p>
      </section>

      <form onSubmit={saveSettings}>
        <label className="checkbox-label">
          <input type="checkbox" checked={automaticCapture} onChange={event => setAutomaticCapture(event.target.checked)} />
          自动捕获网页划词（默认关闭，可与旧 Translator 插件并存）
        </label>
        <label>
          English pronunciation
          <select
            value={settings.pronunciationLanguage}
            onChange={(event) =>
              setSettings((current) => ({
                ...current,
                pronunciationLanguage: event.target.value as TranslatorSettings['pronunciationLanguage'],
              }))
            }
          >
            <option value="en-US">US English</option>
            <option value="en-GB">UK English</option>
          </select>
        </label>

        <label>
          <span>
            <input
              type="checkbox"
              checked={settings.azureFallbackEnabled}
              onChange={(event) =>
                setSettings((current) => ({
                  ...current,
                  azureFallbackEnabled: event.target.checked,
                }))
              }
            />{' '}
            Use Azure only if local translation fails
          </span>
        </label>

        <label>
          Optional Azure Translator key
          <input
            type="password"
            value={settings.azureKey}
            disabled={!settings.azureFallbackEnabled}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) =>
              setSettings((current) => ({ ...current, azureKey: event.target.value }))
            }
          />
        </label>
        <p className="notice">
          Azure is optional. Its key is saved only in Chrome extension storage, but it is not encrypted by Qtranslator. Leave it blank and keep fallback off if you do not need it.
        </p>

        <label>
          Optional Azure region
          <input
            type="text"
            value={settings.azureRegion}
            disabled={!settings.azureFallbackEnabled}
            placeholder="for example: westus2"
            autoComplete="off"
            spellCheck={false}
            onChange={(event) =>
              setSettings((current) => ({ ...current, azureRegion: event.target.value }))
            }
          />
        </label>

        <button type="submit">Save settings</button>
        <output aria-live="polite">{status}</output>
      </form>

      <section className="privacy-card">
        <h2>Privacy and local data</h2>
        <p>
          Qtranslator does not upload favorites, settings, browsing history, complete webpages, screenshots, or audio. Azure receives selected English text only when you explicitly enable it and Chrome local translation fails.
        </p>
        <button
          className={clearArmed ? 'danger armed' : 'danger'}
          type="button"
          onClick={() => void clearLocalData()}
        >
          {clearArmed ? 'Confirm clear local data' : 'Clear local data'}
        </button>
        {clearArmed ? (
          <button className="secondary" type="button" onClick={() => setClearArmed(false)}>
            Cancel
          </button>
        ) : null}
      </section>
    </main>
  );
}
