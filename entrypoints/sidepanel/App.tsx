import { useEffect, useRef, useState } from 'react';
import { FavoritesTransferControls } from './FavoritesTransferControls';


import {
  addFavorite,
  FAVORITES_STORAGE_KEY,
  findFavorite,
  removeFavorite,
  type FavoriteEntry,
} from '../../src/core/favorites';
import {
  FAVORITES_SYNC_METADATA_KEY,
  INITIAL_FAVORITES_SYNC_METADATA,
  parseFavoritesSyncMetadata,
  retryWithDelays,
  synchronizeFavorites,
  type FavoritesSyncMetadata,
} from '../../src/core/favorites-sync';
import {
  patchSharedFavorites,
  readSharedFavorites,
} from '../../src/core/shared-favorites';
import {
  isExtensionMessage,
  type ExtensionResponse,
  type GetLatestTranslationMessage,
  type SelectionTranslation,
} from '../../src/core/messages';
import {
  hasTranslatorSettings,
  TRANSLATOR_SETTINGS_KEY,
  type TranslatorSettings,
} from '../../src/core/settings';
import { browserSpeechPlayer } from '../../src/core/speech';
import {
  startStage2TranslationWorker,
  STAGE2_VISIBLE_TRANSLATOR_PORT,
} from '../../src/core/stage2-translation-worker';
import { createAzureTranslationProvider } from '../../src/providers/azure-translation-provider';
import { chromeTranslationProvider } from '../../src/providers/chrome-translation-provider';
import type { TranslationResult } from '../../src/providers/translation-provider';

startStage2TranslationWorker(STAGE2_VISIBLE_TRANSLATOR_PORT);

function selectionFromResponse(response: ExtensionResponse): SelectionTranslation | null {
  if (!response.ok || !('data' in response) || response.data === null) {
    return null;
  }

  return 'selection' in response.data ? response.data : null;
}

async function translateWithOptionalFallback(text: string): Promise<TranslationResult> {
  const request = {
    text,
    sourceLanguage: 'en' as const,
    targetLanguage: 'zh-CN' as const,
  };

  try {
    return await chromeTranslationProvider.translate(request);
  } catch (localError) {
    const stored = await browser.storage.local.get(TRANSLATOR_SETTINGS_KEY);
    const settings = stored[TRANSLATOR_SETTINGS_KEY] as TranslatorSettings | undefined;

    if (settings?.azureFallbackEnabled && hasTranslatorSettings(settings)) {
      return createAzureTranslationProvider(settings).translate(request);
    }

    throw localError instanceof Error
      ? localError
      : new Error('Chrome local translation failed.');
  }
}

function formatFavoriteTime(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function App() {
  const [latest, setLatest] = useState<SelectionTranslation | null>(null);
  const [favorites, setFavorites] = useState<FavoriteEntry[]>([]);
  const [showFavorites, setShowFavorites] = useState(false);
  const [status, setStatus] = useState('Select English text on a webpage');
  const [foundationResult, setFoundationResult] = useState<string>();
  const [outputIsError, setOutputIsError] = useState(false);
  const [speaking, setSpeaking] = useState(false);
  const [favoritesSyncStatus, setFavoritesSyncStatus] = useState<
    'syncing' | 'shared' | 'browser'
  >('browser');
  const [favoritesSyncError, setFavoritesSyncError] = useState<string>();
  const requestId = useRef(0);
  const speechRequestId = useRef(0);
  const favoritesRef = useRef<FavoriteEntry[]>([]);
  const favoritesSyncMetadata = useRef<FavoritesSyncMetadata>({
    ...INITIAL_FAVORITES_SYNC_METADATA,
  });
  const favoritesSyncInFlight = useRef<Promise<void> | null>(null);
  const favoritesPatchQueue = useRef<Promise<void>>(Promise.resolve());
  const favoriteMutationVersion = useRef(0);
  const lastSharedSyncAttemptAt = useRef(0);

  function applyFavorites(nextFavorites: FavoriteEntry[]) {
    favoritesRef.current = nextFavorites;
    setFavorites(nextFavorites);
  }

  async function saveFavoritesSnapshot(
    nextFavorites: FavoriteEntry[],
    metadata: FavoritesSyncMetadata,
  ) {
    await browser.storage.local.set({
      [FAVORITES_STORAGE_KEY]: nextFavorites,
      [FAVORITES_SYNC_METADATA_KEY]: metadata,
    });
    favoritesSyncMetadata.current = metadata;
    applyFavorites(nextFavorites);
  }

  async function syncSharedFavorites(
    withStartupRetry = false,
    force = false,
  ): Promise<void> {
    if (favoritesSyncInFlight.current) {
      return favoritesSyncInFlight.current;
    }
    if (
      !withStartupRetry &&
      !force &&
      Date.now() - lastSharedSyncAttemptAt.current < 30_000
    ) {
      return;
    }

    lastSharedSyncAttemptAt.current = Date.now();
    const operation = (async () => {
      setFavoritesSyncStatus('syncing');
      setFavoritesSyncError(undefined);
      try {
        const synchronize = () =>
          synchronizeFavorites({
            browserFavorites: favoritesRef.current,
            metadata: favoritesSyncMetadata.current,
            readShared: readSharedFavorites,
            patchShared: patchSharedFavorites,
          });
        const result = withStartupRetry
          ? await retryWithDelays(synchronize)
          : await synchronize();
        await saveFavoritesSnapshot(result.favorites, result.metadata);
        setFavoritesSyncStatus('shared');
      } catch (error) {
        setFavoritesSyncStatus('browser');
        setFavoritesSyncError(
          error instanceof Error
            ? error.message
            : 'The Windows favorites bridge could not be reached.',
        );
      }
    })().finally(() => {
      favoritesSyncInFlight.current = null;
    });

    favoritesSyncInFlight.current = operation;
    return operation;
  }

  async function performTranslation(selectionState: SelectionTranslation) {
    const currentRequest = ++requestId.current;
    const pending = {
      ...selectionState,
      translation: null,
      error: null,
    };

    speechRequestId.current += 1;
    try {
      browserSpeechPlayer.stop();
    } catch {
      // Translation remains available when speech synthesis is unsupported.
    }
    setSpeaking(false);
    setFoundationResult(undefined);
    setOutputIsError(false);
    setLatest(pending);
    setStatus('Preparing local translation…');

    try {
      const translation = await translateWithOptionalFallback(selectionState.selection.text);

      if (currentRequest !== requestId.current) {
        return;
      }

      setLatest({ ...pending, translation });
      setStatus(
        translation.provider === 'azure'
          ? 'Translation ready (Azure fallback)'
          : 'Translation ready locally',
      );
    } catch (error) {
      if (currentRequest !== requestId.current) {
        return;
      }

      const message =
        error instanceof Error ? error.message : 'Chrome local translation failed.';
      setLatest({ ...pending, error: message });
      setStatus(message);
      setFoundationResult(`Translation error: ${message}`);
      setOutputIsError(true);
    }
  }

  useEffect(() => {
    let active = true;

    void browser.storage.local
      .get([FAVORITES_STORAGE_KEY, FAVORITES_SYNC_METADATA_KEY])
      .then(async (stored) => {
        if (!active) {
          return;
        }

        const saved = stored[FAVORITES_STORAGE_KEY];
        const browserFavorites = Array.isArray(saved) ? saved as FavoriteEntry[] : [];
        favoritesSyncMetadata.current = parseFavoritesSyncMetadata(
          stored[FAVORITES_SYNC_METADATA_KEY],
        );
        applyFavorites(browserFavorites);
        await syncSharedFavorites(true);
      })
      .catch(() => {
        if (!active) {
          return;
        }

        const message =
          'Favorites could not be loaded. Check browser storage and reopen Qtranslator.';
        setStatus(message);
        setFoundationResult(message);
        setOutputIsError(true);
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    let active = true;
    const applySelection = (selection: SelectionTranslation) => {
      if (!active) {
        return;
      }

      if (selection.translation) {
        setFoundationResult(undefined);
        setOutputIsError(false);
        setLatest(selection);
        setStatus('Translation ready');
        return;
      }

      void performTranslation(selection);
    };
    const request: GetLatestTranslationMessage = {
      type: 'GET_LATEST_TRANSLATION',
    };

    void browser.runtime.sendMessage(request).then((response: ExtensionResponse) => {
      const selection = selectionFromResponse(response);
      if (selection) {
        applySelection(selection);
      }
    });

    const onMessage = (message: unknown) => {
      if (
        isExtensionMessage(message) &&
        message.type === 'TRANSLATION_UPDATED'
      ) {
        applySelection(message.payload);
      }
    };

    browser.runtime.onMessage.addListener(onMessage);

    return () => {
      active = false;
      requestId.current += 1;
      browser.runtime.onMessage.removeListener(onMessage);
    };
  }, []);

  async function persistFavorites(nextFavorites: FavoriteEntry[]) {
    const nextIds = new Set(nextFavorites.map((favorite) => favorite.id));
    const currentFavorites = favoritesRef.current;
    const currentById = new Map(
      currentFavorites.map((favorite) => [favorite.id, favorite]),
    );
    const mustUploadBrowserFallback =
      favoritesSyncMetadata.current.dirty ||
      !favoritesSyncMetadata.current.migrationCompleted;
    const upsert = mustUploadBrowserFallback
      ? nextFavorites
      : nextFavorites.filter(
          (favorite) =>
            JSON.stringify(currentById.get(favorite.id)) !== JSON.stringify(favorite),
        );
    const removeIds = currentFavorites
      .filter((favorite) => !nextIds.has(favorite.id))
      .map((favorite) => favorite.id);

    try {
      await saveFavoritesSnapshot(nextFavorites, {
        ...favoritesSyncMetadata.current,
        dirty: true,
      });
    } catch {
      const message =
        'Favorites could not be saved. Check browser storage and try again.';
      setStatus(message);
      setFoundationResult(message);
      setOutputIsError(true);
      throw new Error(message);
    }

    const mutationVersion = ++favoriteMutationVersion.current;
    setFavoritesSyncStatus('syncing');
    setFavoritesSyncError(undefined);
    favoritesPatchQueue.current = favoritesPatchQueue.current
      .catch(() => undefined)
      .then(async () => {
        try {
          const sharedFavorites = await patchSharedFavorites(upsert, removeIds);
          if (mutationVersion === favoriteMutationVersion.current) {
            await saveFavoritesSnapshot(sharedFavorites, {
              migrationCompleted: true,
              dirty: false,
            });
            setFavoritesSyncStatus('shared');
          }
        } catch (error) {
          if (mutationVersion === favoriteMutationVersion.current) {
            setFavoritesSyncStatus('browser');
            setFavoritesSyncError(
              error instanceof Error
                ? error.message
                : 'The Windows favorites bridge could not be reached.',
            );
          }
        }
      });
  }

  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState === 'visible') void syncSharedFavorites();
    };
    const onFocus = () => void syncSharedFavorites();
    window.addEventListener('focus', onFocus);
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      window.removeEventListener('focus', onFocus);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, []);

  async function toggleCurrentFavorite() {
    const translation = latest?.translation;
    if (!translation) {
      return;
    }

    const existing = findFavorite(favorites, translation);
    const nextFavorites = existing
      ? removeFavorite(favorites, existing.id)
      : addFavorite(favorites, translation);

    try {
      await persistFavorites(nextFavorites);
      setStatus(existing ? 'Removed from favorites' : 'Saved locally to favorites');
    } catch {
      // persistFavorites already displays a recoverable storage error.
    }
  }

  async function removeSavedFavorite(id: string) {
    try {
      await persistFavorites(removeFavorite(favorites, id));
      setStatus('Removed from favorites');
    } catch {
      // persistFavorites already displays a recoverable storage error.
    }
  }

  async function togglePronunciation(text: string) {
    if (speaking) {
      speechRequestId.current += 1;
      browserSpeechPlayer.stop();
      setSpeaking(false);
      setStatus('Pronunciation stopped');
      return;
    }

    const currentRequest = ++speechRequestId.current;
    setFoundationResult(undefined);
    setOutputIsError(false);
    setSpeaking(true);
    setStatus('Playing English pronunciation…');

    try {
      await browserSpeechPlayer.play(text);

      if (currentRequest !== speechRequestId.current) {
        return;
      }

      setSpeaking(false);
      setStatus('Pronunciation finished');
    } catch (error) {
      if (currentRequest !== speechRequestId.current) {
        return;
      }

      const message =
        error instanceof Error ? error.message : 'Pronunciation failed.';
      setSpeaking(false);
      setStatus(message);
      setFoundationResult(message);
      setOutputIsError(true);
    }
  }

  async function runFoundationCheck() {
    setStatus('Checking local translation…');
    setFoundationResult(undefined);
    setOutputIsError(false);

    try {
      await chromeTranslationProvider.translate({
        text: 'hello',
        sourceLanguage: 'en',
        targetLanguage: 'zh-CN',
      });
      setStatus('Local translation is working');
    } catch (error) {
      const message =
        error instanceof Error ? error.message : 'Local translation check failed';
      setStatus(message);
      setFoundationResult(`Local translation check failed: ${message}`);
      setOutputIsError(true);
    }
  }

  const currentFavorite = latest?.translation
    ? findFavorite(favorites, latest.translation)
    : undefined;
  const wordFavorites = favorites.filter((favorite) => favorite.kind === 'word');
  const sentenceFavorites = favorites.filter(
    (favorite) => favorite.kind === 'sentence',
  );

  return (
    <main className="panel">
      <p className="eyebrow">Version {browser.runtime.getManifest().version}</p>
      <h1>Qtranslator</h1>
      <p className="intro">
        Translate locally, then keep useful words and sentences in this browser.
      </p>
      <button
        className="secondary favorites-trigger"
        type="button"
        onClick={() => {
          setShowFavorites(true);
          void syncSharedFavorites(false, true);
        }}
      >
        Open favorites ({favorites.length})
      </button>

      <section className="card status-card" aria-live="polite">
        <span className="label">Status</span>
        <strong className={latest?.error ? 'error' : undefined}>{status}</strong>
        {latest && !latest.translation ? (
          <button
            className="link-button"
            type="button"
            onClick={() => void performTranslation(latest)}
          >
            Translate / retry
          </button>
        ) : null}
        {latest?.error ? (
          <button
            className="link-button"
            type="button"
            onClick={() => browser.runtime.openOptionsPage()}
          >
            Optional fallback settings
          </button>
        ) : null}
      </section>

      {latest?.translation ? (
        <section className="translation" aria-live="polite">
          <div className="text-block">
            <span className="label">Selected English</span>
            <p lang="en">{latest.selection.text}</p>
            {latest.translation.phonetic ? (
              <p className="phonetic">{latest.translation.phonetic}</p>
            ) : null}
            <button
              className="speech-button secondary"
              type="button"
              aria-label={speaking ? 'Stop pronunciation' : 'Play English pronunciation'}
              onClick={() => void togglePronunciation(latest.selection.text)}
            >
              {speaking ? '■ Stop pronunciation' : '▶ Play pronunciation'}
            </button>
          </div>
          <div className="text-block result">
            <span className="label">Chinese translation</span>
            <p lang="zh-CN">{latest.translation.translatedText}</p>
          </div>
          <button
            className={`heart-button${currentFavorite ? ' saved' : ''}`}
            type="button"
            aria-label={currentFavorite ? 'Remove from favorites' : 'Save to favorites'}
            title={currentFavorite ? 'Saved to favorites' : 'Save to favorites'}
            onClick={() => void toggleCurrentFavorite()}
          >
            <span aria-hidden="true">{currentFavorite ? '♥' : '♡'}</span>
          </button>
          <dl>
            <div>
              <dt>Type</dt>
              <dd>{latest.translation.textKind}</dd>
            </div>
            <div>
              <dt>Provider</dt>
              <dd>
                {latest.translation.provider === 'chrome-local'
                  ? 'Chrome local'
                  : 'Azure fallback'}
              </dd>
            </div>
            <div>
              <dt>Source</dt>
              <dd title={latest.selection.pageUrl}>
                {latest.selection.pageTitle || 'Current webpage'}
              </dd>
            </div>
          </dl>
        </section>
      ) : (
        <section className="empty-state">
          <strong>{latest?.error ? 'Translation unavailable' : 'No selection yet'}</strong>
          <span>
            {latest?.error
              ? 'Click Translate / retry. Chrome may need that click to download the language pack.'
              : 'Highlight a word, sentence, or paragraph on a normal webpage.'}
          </span>
        </section>
      )}

      {showFavorites ? (
        <div className="favorites-overlay" role="dialog" aria-modal="true" aria-labelledby="favorites-heading">
          <section className="favorites">
            <div className="section-heading">
              <div>
                <h2 id="favorites-heading">Favorites</h2>
                <span>{favorites.length} saved locally</span>
                <span>
                  {favoritesSyncStatus === 'shared'
                    ? 'Shared with the Windows app'
                    : favoritesSyncStatus === 'syncing'
                      ? 'Checking shared favorites…'
                      : 'Browser storage · sync paused'}
                </span>
                {favoritesSyncError ? (
                  <span className="error">
                    Sync failed: {favoritesSyncError}
                  </span>
                ) : null}
              </div>
              <button
                className="link-button"
                type="button"
                disabled={favoritesSyncStatus === 'syncing'}
                onClick={() => void syncSharedFavorites(false, true)}
              >
                Sync now
              </button>
              <button
                className="close-button"
                type="button"
                aria-label="Close favorites"
                onClick={() => setShowFavorites(false)}
              >
                ×
                                    </button>
            </div>
        <FavoritesTransferControls
          favorites={favorites}
          onImport={persistFavorites}
          onStatus={(message, isError = false) => {
            setStatus(message);
            setFoundationResult(message);
            setOutputIsError(isError);
          }}
        />


        <div className="favorite-group">
          <h3>Words ({wordFavorites.length})</h3>
          {wordFavorites.length ? (
            <ul>
              {wordFavorites.map((favorite) => (
                <li key={favorite.id}>
                  <div>
                    <strong lang="en">{favorite.originalText}</strong>
                    {favorite.phonetic ? (
                      <span className="phonetic">{favorite.phonetic}</span>
                    ) : null}
                    <span lang="zh-CN">{favorite.translatedText}</span>
                    <time dateTime={favorite.firstFavoritedAt}>
                      First saved {formatFavoriteTime(favorite.firstFavoritedAt)}
                    </time>
                  </div>
                  <button
                    className="remove-button"
                    type="button"
                    aria-label={`Remove ${favorite.originalText}`}
                    onClick={() => void removeSavedFavorite(favorite.id)}
                  >
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className="muted">No saved words yet.</p>
          )}
        </div>

        <div className="favorite-group">
          <h3>Sentences ({sentenceFavorites.length})</h3>
          {sentenceFavorites.length ? (
            <ul>
              {sentenceFavorites.map((favorite) => (
                <li key={favorite.id}>
                  <div>
                    <strong lang="en">{favorite.originalText}</strong>
                    <span lang="zh-CN">{favorite.translatedText}</span>
                    <time dateTime={favorite.firstFavoritedAt}>
                      First saved {formatFavoriteTime(favorite.firstFavoritedAt)}
                    </time>
                  </div>
                  <button
                    className="remove-button"
                    type="button"
                    aria-label="Remove saved sentence"
                    onClick={() => void removeSavedFavorite(favorite.id)}
                  >
                    Remove
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p className="muted">No saved sentences yet.</p>
          )}
        </div>
          </section>
        </div>
      ) : null}

      <button className="secondary" type="button" onClick={runFoundationCheck}>
        Run local translation check
      </button>
      {foundationResult ? (
        <output className={outputIsError ? 'error' : undefined} aria-live="polite">
          {foundationResult}
        </output>
      ) : null}
    </main>
  );
}
