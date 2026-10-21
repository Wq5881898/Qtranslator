import type { FavoriteEntry } from './favorites';
import { mergeFavorites } from './favorites-transfer';

export const FAVORITES_SYNC_METADATA_KEY = 'translator.favoritesSync.v1';
export const FAVORITES_STARTUP_RETRY_DELAYS_MS = [0, 1_000, 3_000, 6_000, 10_000] as const;

export interface FavoritesSyncMetadata {
  migrationCompleted: boolean;
  dirty: boolean;
}

export interface FavoritesSyncResult {
  favorites: FavoriteEntry[];
  metadata: FavoritesSyncMetadata;
}

export const INITIAL_FAVORITES_SYNC_METADATA: FavoritesSyncMetadata = {
  migrationCompleted: false,
  dirty: false,
};

export function parseFavoritesSyncMetadata(value: unknown): FavoritesSyncMetadata {
  if (!value || typeof value !== 'object') {
    return { ...INITIAL_FAVORITES_SYNC_METADATA };
  }

  const candidate = value as Partial<FavoritesSyncMetadata>;
  return {
    migrationCompleted: candidate.migrationCompleted === true,
    dirty: candidate.dirty === true,
  };
}

export async function synchronizeFavorites(options: {
  browserFavorites: FavoriteEntry[];
  metadata: FavoritesSyncMetadata;
  readShared: () => Promise<FavoriteEntry[]>;
  patchShared: (upsert: FavoriteEntry[], removeIds?: string[]) => Promise<FavoriteEntry[]>;
}): Promise<FavoritesSyncResult> {
  const shared = await options.readShared();
  const requiresMerge = !options.metadata.migrationCompleted || options.metadata.dirty;
  const favorites = requiresMerge
    ? await options.patchShared(mergeFavorites(shared, options.browserFavorites))
    : shared;

  return {
    favorites,
    metadata: {
      migrationCompleted: true,
      dirty: false,
    },
  };
}

export async function retryWithDelays<T>(
  task: () => Promise<T>,
  delays: readonly number[] = FAVORITES_STARTUP_RETRY_DELAYS_MS,
  wait: (milliseconds: number) => Promise<void> = (milliseconds) =>
    new Promise((resolve) => globalThis.setTimeout(resolve, milliseconds)),
): Promise<T> {
  let lastError: unknown;

  for (const delay of delays) {
    if (delay > 0) {
      await wait(delay);
    }
    try {
      return await task();
    } catch (error) {
      lastError = error;
    }
  }

  throw lastError instanceof Error
    ? lastError
    : new Error('The shared favorites service is unavailable.');
}
