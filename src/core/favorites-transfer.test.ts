import { describe, expect, it } from 'vitest';

import type { FavoriteEntry } from './favorites';
import {
  mergeFavorites,
  parseFavoritesCsv,
  serializeFavoritesCsv,
} from './favorites-transfer';

const word: FavoriteEntry = {
  id: 'word:hello',
  kind: 'word',
  originalText: 'Hello',
  translatedText: '你好，世界',
  firstFavoritedAt: '2026-07-28T10:00:00.000Z',
  phonetic: '/həˈləʊ/',
};

const sentence: FavoriteEntry = {
  id: 'sentence:He said "hello".',
  kind: 'sentence',
  originalText: 'He said "hello".',
  translatedText: '他说：“你好”。',
  firstFavoritedAt: '2026-07-28T11:00:00.000Z',
};

describe('favorites CSV transfer', () => {
  it('round-trips an Excel-friendly CSV with commas and quotes', () => {
    const text = serializeFavoritesCsv([word, sentence]);

    expect(text.startsWith('\uFEFFType,English,Phonetic,Chinese translation,First saved')).toBe(true);
    expect(text).toContain('2026-07-28\r\n');
    expect(text).not.toContain('2026-07-28T10:00:00.000Z');
    expect(parseFavoritesCsv(text)).toEqual([
      { ...word, firstFavoritedAt: '2026-07-28' },
      { ...sentence, firstFavoritedAt: '2026-07-28' },
    ]);
  });

  it('rejects unrelated or malformed CSV without partial data', () => {
    expect(() => parseFavoritesCsv('name,value\nhello,你好')).toThrow(
      'valid Translator favorites CSV file',
    );
    expect(() =>
      parseFavoritesCsv(
        'Type,English,Phonetic,Chinese translation,First saved\nword,hello,,你好,not-a-date',
      ),
    ).toThrow('CSV row 2 is invalid');
  });

  it('identifies the edited row when First saved is missing', () => {
    const editedCsv =
      'Type,English,Phonetic,Chinese translation,First saved\n' +
      'sentence,read more abo,,阅读更多 ABO,2026-07-29T02:42:33.626Z\n' +
      'sentence,We do not offer this definition as,,我们不提供此定义为,2026-07-29T02:42:30.777Z\n' +
      'sentence,Merriam-Webster’s Great Big List of Words You Love to Hate,,Merriam-Webster 的一大堆你喜欢讨厌的词,2026-07-29T02:42:27.574Z\n' +
      'word,baby,,娃娃,\n';

    expect(() => parseFavoritesCsv(editedCsv)).toThrow(
      'CSV row 5 is invalid. Type, English, Chinese translation, and First saved are required.',
    );
  });

  it('normalizes imported ids and removes duplicates', () => {
    const imported = parseFavoritesCsv(
      'Type,English,Phonetic,Chinese translation,First saved\n' +
        'word,Hello,/həˈləʊ/,你好,2026-07-28T10:00:00.000Z\n' +
        'word, hello ,/həˈləʊ/,您好,2026-07-28T12:00:00.000Z\n',
    );

    expect(imported).toHaveLength(1);
    expect(imported[0]?.id).toBe('word:hello');
  });

  it('merges new entries while preserving existing entries', () => {
    expect(mergeFavorites([word], [{ ...word, translatedText: 'different' }, sentence])).toEqual([
      word,
      sentence,
    ]);
  });
});
