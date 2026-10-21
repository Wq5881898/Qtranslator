import { describe, expect, it, vi } from 'vitest';

import { convertArpabetToIpa, fetchEnglishDictionaryLookup, normalizePhonetic } from './free-dictionary-provider';

describe('free dictionary phonetics', () => {
  it('renders the consultation phonetic without a missing combining glyph', async () => {
    const fetcher = async () =>
      new Response(
        JSON.stringify([
          {
            word: 'consultation',
            phonetic: '/ˌkɒnsl̩ˈteɪʃən/',
            meanings: [{ definitions: [{ definition: 'The act of consulting.' }] }],
          },
        ]),
      );

    const result = await fetchEnglishDictionaryLookup('consultation', fetcher);

    expect(result?.phonetic).toBe('/ˌkɒnsəlˈteɪʃən/');
  });

  it('rejects a corrupted phonetic and uses the next valid dictionary value', async () => {
    const fetcher = async () =>
      new Response(
        JSON.stringify([
          {
            word: 'word',
            phonetic: '/wɜːd�/',
            phonetics: [{ text: '/wɜːd/' }],
            meanings: [],
          },
        ]),
      );

    const result = await fetchEnglishDictionaryLookup('word', fetcher);

    expect(result?.phonetic).toBe('/wɜːd/');
  });

  it('normalizes other syllabic consonants without altering ordinary IPA', () => {
    expect(normalizePhonetic('/ˈbʌtn̩/')).toBe('/ˈbʌtən/');
    expect(normalizePhonetic('/həˈləʊ/')).toBe('/həˈləʊ/');
    expect(normalizePhonetic('/pɹaʊd/')).toBe('/praʊd/');
    expect(normalizePhonetic('/kɹaʊt͡ʃ/')).toBe('/kraʊtʃ/');
    expect(normalizePhonetic('/d͜ʒʌdʒ/')).toBe('/dʒʌdʒ/');
  });

  it('converts Datamuse ARPAbet into IPA instead of displaying pronunciation codes', () => {
    expect(convertArpabetToIpa('CH EY1 N JH AH0 Z')).toBe('/ˈtʃeɪndʒəz/');
    expect(normalizePhonetic('CH EY1 N JH AH0 Z')).toBe('/ˈtʃeɪndʒəz/');
    expect(normalizePhonetic('CH EY1 UNKNOWN')).toBeUndefined();
  });

  it('converts ARPAbet returned by the Datamuse fallback for changes', async () => {
    const fetcher = vi.fn(async (url: string) => {
      if (url.includes('dictionaryapi.dev')) return new Response('', { status: 404 });
      return new Response(JSON.stringify([{
        word: 'changes',
        tags: ['n', 'pron:CH EY1 N JH AH0 Z'],
        defs: ['n\tthe act or result of becoming different'],
      }]));
    });
    const result = await fetchEnglishDictionaryLookup('changes', fetcher);
    expect(result?.phonetic).toBe('/ˈtʃeɪndʒəz/');
  });

  it('keeps a valid original entry instead of replacing during with dur', async () => {
    const fetcher = vi.fn(async (url: string) => {
      if (!url.endsWith('/during')) throw new Error(`unexpected lemma request: ${url}`);
      return new Response(JSON.stringify([{ word: 'during', phonetic: '/ˈdjʊəɹɪŋ/', meanings: [{ definitions: [{ definition: 'Throughout the course of.' }] }] }]));
    });

    const result = await fetchEnglishDictionaryLookup('During', fetcher);

    expect(result).toEqual({
      headword: 'during',
      phonetic: '/ˈdjʊərɪŋ/',
      definitions: ['Throughout the course of.'],
      senses: [{ definition: 'Throughout the course of.' }],
    });
    expect(fetcher).toHaveBeenCalledOnce();
  });

  it('prefers an explicitly tagged US pronunciation over an uncommon first variant', async () => {
    const fetcher = vi.fn(async () => new Response(JSON.stringify([{
      word: 'during',
      phonetic: '/ˈdjɔː.ɹɪŋ/',
      phonetics: [
        { text: '/ˈdjɔː.ɹɪŋ/' },
        { text: '/ˈd(j)ʊɚ.ɪŋ/', audio: 'https://example.test/during-us.mp3' },
      ],
      meanings: [{ definitions: [{ definition: 'For all of a given time interval.' }] }],
    }])));

    const result = await fetchEnglishDictionaryLookup('during', fetcher);

    expect(result?.phonetic).toBe('/ˈdjʊərɪŋ/');
  });

  it('retries one transient first lookup failure before falling back', async () => {
    const fetcher = vi.fn()
      .mockRejectedValueOnce(new TypeError('temporary network failure'))
      .mockResolvedValueOnce(new Response(JSON.stringify([{ word: 'proud', phonetic: '/pɹaʊd/', meanings: [{ definitions: [{ definition: 'Feeling deep pleasure or satisfaction.' }] }, { definitions: [{ definition: 'Having a high opinion of oneself.' }] }] }])));

    const result = await fetchEnglishDictionaryLookup('proud', fetcher);

    expect(result).toMatchObject({
      headword: 'proud',
      phonetic: '/praʊd/',
      definitions: ['Feeling deep pleasure or satisfaction.', 'Having a high opinion of oneself.'],
    });
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it('stops after one quick retry so a failed dictionary cannot block translation', async () => {
    const fetcher = vi.fn()
      .mockRejectedValueOnce(new TypeError('first transient failure'))
      .mockRejectedValueOnce(new TypeError('second transient failure'))
      .mockResolvedValueOnce(new Response(JSON.stringify([{ word: 'crouch', phonetic: '/kɹaʊt͡ʃ/', meanings: [{ definitions: [{ definition: 'To bend low.' }] }] }])));

    await expect(fetchEnglishDictionaryLookup('crouch', fetcher)).rejects.toThrow(
      'first transient failure',
    );
    expect(fetcher).toHaveBeenCalledTimes(3);
  });

  it('uses Datamuse IPA, parts of speech, and definitions when the primary dictionary fails', async () => {
    const fetcher = vi.fn(async (url: string) => {
      if (url.includes('dictionaryapi.dev')) {
        throw new TypeError('primary unavailable');
      }
      return new Response(JSON.stringify([{
        word: 'baby',
        tags: ['n', 'v', 'pron:/ˈbeɪbi/'],
        defs: ['n\ta very young child', 'v\tto treat someone as a baby'],
      }]));
    });

    const result = await fetchEnglishDictionaryLookup('baby', fetcher);

    expect(result).toEqual({
      headword: 'baby',
      phonetic: '/ˈbeɪbi/',
      definitions: ['a very young child', 'to treat someone as a baby'],
      senses: [
        { partOfSpeech: 'noun', definition: 'a very young child' },
        { partOfSpeech: 'verb', definition: 'to treat someone as a baby' },
      ],
    });
  });
});
