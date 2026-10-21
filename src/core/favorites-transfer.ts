import type { FavoriteEntry, FavoriteKind } from './favorites';

const CSV_HEADERS = [
  'Type',
  'English',
  'Phonetic',
  'Chinese translation',
  'First saved',
] as const;

function normalizedId(text: string, kind: FavoriteKind): string {
  const normalized = text.replace(/\s+/gu, ' ').trim();
  return `${kind}:${kind === 'word' ? normalized.toLocaleLowerCase('en') : normalized}`;
}

function csvCell(value: string): string {
  return /[",\r\n]/u.test(value) ? `"${value.replace(/"/gu, '""')}"` : value;
}

function favoriteDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value.slice(0, 10) : date.toISOString().slice(0, 10);
}

function parseCsvRows(text: string): string[][] {
  const source = text.replace(/^\uFEFF/u, '');
  const rows: string[][] = [];
  let row: string[] = [];
  let value = '';
  let quoted = false;

  for (let index = 0; index < source.length; index += 1) {
    const character = source[index];

    if (quoted) {
      if (character === '"' && source[index + 1] === '"') {
        value += '"';
        index += 1;
      } else if (character === '"') {
        quoted = false;
      } else {
        value += character;
      }
      continue;
    }

    if (character === '"' && value === '') {
      quoted = true;
    } else if (character === ',') {
      row.push(value);
      value = '';
    } else if (character === '\n') {
      row.push(value.replace(/\r$/u, ''));
      rows.push(row);
      row = [];
      value = '';
    } else {
      value += character;
    }
  }

  if (quoted) {
    throw new Error('The CSV file contains an unfinished quoted value.');
  }

  if (value || row.length) {
    row.push(value.replace(/\r$/u, ''));
    rows.push(row);
  }

  return rows.filter((item) => item.some((cell) => cell.trim()));
}

export function serializeFavoritesCsv(favorites: FavoriteEntry[]): string {
  const rows = favorites.map((favorite) => [
    favorite.kind,
    favorite.originalText,
    favorite.phonetic ?? '',
    favorite.translatedText,
    favoriteDate(favorite.firstFavoritedAt),
  ]);

  return `\uFEFF${[CSV_HEADERS, ...rows]
    .map((row) => row.map((cell) => csvCell(cell)).join(','))
    .join('\r\n')}\r\n`;
}

export function parseFavoritesCsv(text: string): FavoriteEntry[] {
  const rows = parseCsvRows(text);
  const headers = rows.shift();

  if (!headers || headers.length !== CSV_HEADERS.length) {
    throw new Error('Choose a valid Translator favorites CSV file.');
  }

  const normalizedHeaders = headers.map((header) => header.trim());
  if (!CSV_HEADERS.every((header, index) => normalizedHeaders[index] === header)) {
    throw new Error('This CSV does not have the expected Translator columns.');
  }

  const unique = new Map<string, FavoriteEntry>();

  for (const [index, row] of rows.entries()) {
    const csvRowNumber = index + 2;
    if (row.length !== CSV_HEADERS.length) {
      throw new Error(`CSV row ${csvRowNumber} has the wrong number of columns.`);
    }

    const [
      rawKind = '',
      rawEnglish = '',
      rawPhonetic = '',
      rawChinese = '',
      rawDate = '',
    ] = row;
    const kind = rawKind.trim();
    const originalText = rawEnglish.trim();
    const translatedText = rawChinese.trim();
    const firstFavoritedAt = rawDate.trim();

    if (
      (kind !== 'word' && kind !== 'sentence') ||
      !originalText ||
      !translatedText ||
      !firstFavoritedAt ||
      Number.isNaN(Date.parse(firstFavoritedAt))
    ) {
      throw new Error(
        `CSV row ${csvRowNumber} is invalid. Type, English, Chinese translation, and First saved are required.`,
      );
    }

    const phonetic = rawPhonetic.trim();
    const id = normalizedId(originalText, kind);
    if (!unique.has(id)) {
      unique.set(id, {
        id,
        kind,
        originalText,
        translatedText,
        firstFavoritedAt,
        ...(phonetic ? { phonetic } : {}),
      });
    }
  }

  return [...unique.values()];
}

export function mergeFavorites(
  current: FavoriteEntry[],
  imported: FavoriteEntry[],
): FavoriteEntry[] {
  const merged = new Map(current.map((favorite) => [favorite.id, favorite]));

  for (const favorite of imported) {
    if (!merged.has(favorite.id)) {
      merged.set(favorite.id, favorite);
    }
  }

  return [...merged.values()];
}
