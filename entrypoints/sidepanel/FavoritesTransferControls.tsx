import { type ChangeEvent, useRef, useState } from 'react';

import type { FavoriteEntry } from '../../src/core/favorites';
import {
  mergeFavorites,
  parseFavoritesCsv,
  serializeFavoritesCsv,
} from '../../src/core/favorites-transfer';

type Props = {
  favorites: FavoriteEntry[];
  onImport(favorites: FavoriteEntry[]): Promise<void>;
  onStatus(message: string, isError?: boolean): void;
};

type ImportStatus = {
  message: string;
  isError: boolean;
};

export function FavoritesTransferControls({ favorites, onImport, onStatus }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [importStatus, setImportStatus] = useState<ImportStatus>();

  function reportStatus(message: string, isError = false) {
    setImportStatus({ message, isError });
    onStatus(message, isError);
  }

  function exportFavorites() {
    const blob = new Blob([serializeFavoritesCsv(favorites)], {
      type: 'text/csv;charset=utf-8',
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    const date = new Date().toISOString().slice(0, 10);
    link.href = url;
    link.download = `qtranslator-favorites-${date}.csv`;
    link.click();
    URL.revokeObjectURL(url);
    reportStatus(`Exported ${favorites.length} favorites to CSV`);
  }

  async function importFavorites(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }

    setBusy(true);
    setImportStatus({ message: `Importing ${file.name}…`, isError: false });
    try {
      const imported = parseFavoritesCsv(await file.text());
      const merged = mergeFavorites(favorites, imported);
      const added = merged.length - favorites.length;
      await onImport(merged);
      reportStatus(
        added > 0
          ? `Imported ${added} new favorites from CSV`
          : 'Import complete; no new favorites found',
      );
    } catch (error) {
      reportStatus(
        error instanceof Error ? error.message : 'Favorites import failed.',
        true,
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="favorites-transfer-section">
      <div className="favorites-transfer">
        <button
          className="secondary"
          type="button"
          disabled={busy}
          onClick={exportFavorites}
        >
          Export CSV
        </button>
        <button
          className="secondary"
          type="button"
          disabled={busy}
          onClick={() => inputRef.current?.click()}
        >
          {busy ? 'Importing…' : 'Import CSV'}
        </button>
        <input
          ref={inputRef}
          className="visually-hidden"
          type="file"
          accept="text/csv,.csv"
          onChange={(event) => void importFavorites(event)}
        />
      </div>
      {importStatus ? (
        <output
          className={`import-status${importStatus.isError ? ' error' : ''}`}
          aria-live="polite"
        >
          {importStatus.message}
        </output>
      ) : null}
    </div>
  );
}
