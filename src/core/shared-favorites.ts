import type { FavoriteEntry } from './favorites';
import {
  createStage2BridgeEnvelope,
  isStage2BridgeEnvelope,
  STAGE2_NATIVE_HOST,
} from './stage2-bridge';

type FavoritesPayload = { favorites: FavoriteEntry[] };

function favoritesFromResponse(value: unknown): FavoriteEntry[] {
  if (!isStage2BridgeEnvelope(value)) {
    throw new Error('The shared favorites service returned an invalid response.');
  }
  if (value.messageType === 'bridge.error') {
    const payload = value.payload as { message?: unknown };
    throw new Error(
      typeof payload.message === 'string'
        ? payload.message
        : 'The shared favorites service failed.',
    );
  }
  const payload = value.payload as Partial<FavoritesPayload>;
  if (value.messageType !== 'favorites.result' || !Array.isArray(payload.favorites)) {
    throw new Error('The shared favorites service returned invalid data.');
  }
  return payload.favorites as FavoriteEntry[];
}

export async function readSharedFavorites(): Promise<FavoriteEntry[]> {
  const request = createStage2BridgeEnvelope('favorites.read', {});
  return favoritesFromResponse(
    await browser.runtime.sendNativeMessage(STAGE2_NATIVE_HOST, request),
  );
}

export async function writeSharedFavorites(
  favorites: FavoriteEntry[],
): Promise<FavoriteEntry[]> {
  const request = createStage2BridgeEnvelope<FavoritesPayload>('favorites.write', {
    favorites,
  });
  return favoritesFromResponse(
    await browser.runtime.sendNativeMessage(STAGE2_NATIVE_HOST, request),
  );
}

export async function patchSharedFavorites(
  upsert: FavoriteEntry[],
  removeIds: string[] = [],
): Promise<FavoriteEntry[]> {
  const request = createStage2BridgeEnvelope('favorites.patch', {
    upsert,
    removeIds,
  });
  return favoritesFromResponse(
    await browser.runtime.sendNativeMessage(STAGE2_NATIVE_HOST, request),
  );
}
