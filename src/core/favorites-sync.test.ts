import { describe, expect, it, vi } from 'vitest';

import type { FavoriteEntry } from './favorites';
import {
  INITIAL_FAVORITES_SYNC_METADATA,
  parseFavoritesSyncMetadata,
  retryWithDelays,
  synchronizeFavorites,
} from './favorites-sync';

const browserFavorite: FavoriteEntry = {
  id: 'word:browser',
  kind: 'word',
  originalText: 'browser',
  translatedText: '浏览器',
  firstFavoritedAt: '2026-07-30T10:00:00.000Z',
};

const sharedFavorite: FavoriteEntry = {
  id: 'word:shared',
  kind: 'word',
  originalText: 'shared',
  translatedText: '共享的',
  firstFavoritedAt: '2026-07-30T11:00:00.000Z',
};

describe('favorites synchronization', () => {
  it('merges browser fallback data on first migration and marks it clean', async () => {
    const patchShared = vi.fn(async (favorites: FavoriteEntry[]) => favorites);
    const result = await synchronizeFavorites({
      browserFavorites: [browserFavorite],
      metadata: INITIAL_FAVORITES_SYNC_METADATA,
      readShared: async () => [sharedFavorite],
      patchShared,
    });

    expect(patchShared).toHaveBeenCalledWith([sharedFavorite, browserFavorite]);
    expect(result.favorites).toEqual([sharedFavorite, browserFavorite]);
    expect(result.metadata).toEqual({ migrationCompleted: true, dirty: false });
  });

  it('treats the shared library as authoritative after a clean migration', async () => {
    const patchShared = vi.fn();
    const result = await synchronizeFavorites({
      browserFavorites: [browserFavorite],
      metadata: { migrationCompleted: true, dirty: false },
      readShared: async () => [sharedFavorite],
      patchShared,
    });

    expect(patchShared).not.toHaveBeenCalled();
    expect(result.favorites).toEqual([sharedFavorite]);
  });

  it('merges browser changes made while the bridge was offline', async () => {
    const patchShared = vi.fn(async (favorites: FavoriteEntry[]) => favorites);
    const result = await synchronizeFavorites({
      browserFavorites: [browserFavorite],
      metadata: { migrationCompleted: true, dirty: true },
      readShared: async () => [sharedFavorite],
      patchShared,
    });

    expect(patchShared).toHaveBeenCalledWith([sharedFavorite, browserFavorite]);
    expect(result.metadata.dirty).toBe(false);
  });

  it('retries only the configured number of times and then stops', async () => {
    const task = vi.fn(async () => {
      throw new Error('offline');
    });
    const wait = vi.fn(async () => undefined);

    await expect(retryWithDelays(task, [0, 10, 20], wait)).rejects.toThrow('offline');
    expect(task).toHaveBeenCalledTimes(3);
    expect(wait.mock.calls).toEqual([[10], [20]]);
  });

  it('stops retrying as soon as the bridge becomes available', async () => {
    let attempts = 0;
    const task = vi.fn(async () => {
      attempts += 1;
      if (attempts === 1) throw new Error('offline');
      return 'connected';
    });
    const wait = vi.fn(async () => undefined);

    await expect(retryWithDelays(task, [0, 10, 20], wait)).resolves.toBe('connected');
    expect(task).toHaveBeenCalledTimes(2);
    expect(wait.mock.calls).toEqual([[10]]);
  });

  it('accepts only explicit persisted metadata flags', () => {
    expect(parseFavoritesSyncMetadata({ migrationCompleted: true, dirty: true })).toEqual({
      migrationCompleted: true,
      dirty: true,
    });
    expect(parseFavoritesSyncMetadata('invalid')).toEqual(INITIAL_FAVORITES_SYNC_METADATA);
  });
});
