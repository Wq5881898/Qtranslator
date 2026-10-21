import { describe, expect, it, vi } from 'vitest';

import {
  createChromeTranslationProvider,
  type BuiltInTranslatorApi,
} from './chrome-translation-provider';

function api(
  availability: 'available' | 'downloadable' | 'downloading' | 'unavailable',
  translatedText = '每天学习英语。',
): BuiltInTranslatorApi {
  return {
    availability: vi.fn(async () => availability),
    create: vi.fn(async () => ({
      translate: vi.fn(async () => translatedText),
    })),
  };
}

describe('ChromeTranslationProvider', () => {
  it('translates with the local English-to-Chinese model', async () => {
    const translatorApi = api('available');
    const provider = createChromeTranslationProvider(translatorApi);

    const result = await provider.translate({
      text: 'Learn English every day.',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(result).toMatchObject({
      translatedText: '每天学习英语。',
      textKind: 'sentence',
      provider: 'chrome-local',
    });
    expect(translatorApi.create).toHaveBeenCalledWith({
      sourceLanguage: 'en',
      targetLanguage: 'zh',
    });
  });

  it('lets Chrome download a language pack when it is downloadable', async () => {
    const translatorApi = api('downloadable', '大家好。');
    const provider = createChromeTranslationProvider(translatorApi);

    const result = await provider.translate({
      text: 'Hello everyone.',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(result.translatedText).toBe('大家好。');
  });

  it('returns a clear unsupported-device error', async () => {
    const provider = createChromeTranslationProvider(api('unavailable'));

    await expect(
      provider.translate({
        text: 'A sentence.',
        sourceLanguage: 'en',
        targetLanguage: 'zh-CN',
      }),
    ).rejects.toThrow('does not support the English-to-Chinese language pack');
  });

  it('uses a dictionary headword and definitions for an inflected ambiguous word', async () => {
    const translate = vi.fn(async (text: string) => {
      if (text.includes('\n')) {
        return '[0] 授予；批准\n[1] 给予或准予，尤其是回应请求\n[2] 承认某事属实';
      }
      const translations: Record<string, string> = {
        grant: '授予；批准',
        'To give or confer, with or without compensation, particularly in answer to prayer or request.':
          '给予或准予，尤其是回应请求',
        'To admit as true what is not yet generally accepted.': '承认某事属实',
      };
      return translations[text] ?? `unexpected:${text}`;
    });
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async (url: string) => {
      if (url.endsWith('/granted')) {
        return new Response(
          JSON.stringify([{ word: 'granted', phonetic: '/ˈɡrɑːntɪd/' }]),
          { status: 200 },
        );
      }
      return new Response(
        JSON.stringify([
          {
            word: 'grant',
            meanings: [
              {
                partOfSpeech: 'verb',
                definitions: [
                  {
                    definition:
                      'To give or confer, with or without compensation, particularly in answer to prayer or request.',
                  },
                ],
              },
              {
                partOfSpeech: 'verb',
                definitions: [
                  { definition: 'To admit as true what is not yet generally accepted.' },
                ],
              },
            ],
          },
        ]),
        { status: 200 },
      );
    });
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const result = await provider.translate({
      text: 'granted',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(result.translatedText).toBe(
      'verb：授予；批准；给予或准予，尤其是回应请求；承认某事属实',
    );
    expect(result.meanings).toEqual([{ partOfSpeech: 'verb', translatedText: '授予；批准；给予或准予，尤其是回应请求；承认某事属实' }]);
    expect(result.phonetic).toBe('/ˈɡrɑːntɪd/');
    expect(result.partsOfSpeech).toEqual(['verb']);
    expect(translate).not.toHaveBeenCalledWith('granted');
    expect(translate).toHaveBeenCalledOnce();
  });

  it('translates a headword and its dictionary senses in one local-model call', async () => {
    let activeTranslations = 0;
    let maximumConcurrency = 0;
    const translate = vi.fn(async (text: string) => {
      activeTranslations += 1;
      maximumConcurrency = Math.max(maximumConcurrency, activeTranslations);
      await Promise.resolve();
      activeTranslations -= 1;
      return `zh:${text}`;
    });
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () =>
      new Response(
        JSON.stringify([
          {
            word: 'whoosh',
            meanings: [
              { definitions: [{ definition: 'To move swiftly with a rushing sound.' }] },
              { definitions: [{ definition: 'A sudden rushing sound.' }] },
            ],
          },
        ]),
        { status: 200 },
      ),
    );
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    await provider.translate({
      text: 'whoosh',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(maximumConcurrency).toBe(1);
    expect(translate).toHaveBeenCalledOnce();
    expect(translate).toHaveBeenCalledWith(
      '[0] whoosh\n[1] To move swiftly with a rushing sound.\n[2] A sudden rushing sound.',
    );
  });

  it('binds each part of speech to its own translated meaning', async () => {
    const translate = vi.fn(async () =>
      '[0] 地址；处理\n[1] 某人居住的地点\n[2] 处理或应对某个问题',
    );
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () => new Response(JSON.stringify([{
      word: 'address',
      phonetic: '/əˈdrɛs/',
      meanings: [
        { partOfSpeech: 'noun', definitions: [{ definition: 'The place where someone lives.' }] },
        { partOfSpeech: 'verb', definitions: [{ definition: 'To deal with a problem.' }] },
      ],
    }])));
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const result = await provider.translate({ text: 'address', sourceLanguage: 'en', targetLanguage: 'zh-CN' });

    expect(result.translatedText).toBe(
      'noun：某人居住的地点\nverb：处理或应对某个问题',
    );
    expect(result.meanings).toEqual([
      { partOfSpeech: 'noun', translatedText: '某人居住的地点' },
      { partOfSpeech: 'verb', translatedText: '处理或应对某个问题' },
    ]);
  });

  it('destroys a failed session and retries once with a fresh session', async () => {
    const destroyFirst = vi.fn();
    const firstTranslate = vi.fn(async () => {
      throw new DOMException('Other generic failures occurred.', 'OperationError');
    });
    const secondTranslate = vi.fn(async () => '嗖的一声');
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi
        .fn()
        .mockResolvedValueOnce({ translate: firstTranslate, destroy: destroyFirst })
        .mockResolvedValueOnce({ translate: secondTranslate, destroy: vi.fn() }),
    };
    const dictionaryFetcher = vi.fn(async () => new Response('', { status: 404 }));
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const recovered = await provider.translate({
      text: 'whoosh',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });
    const next = await provider.translate({
      text: 'hello',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(recovered.translatedText).toBe('嗖的一声');
    expect(next.translatedText).toBe('嗖的一声');
    expect(destroyFirst).toHaveBeenCalledOnce();
    expect(translatorApi.create).toHaveBeenCalledTimes(2);
  });

  it('serializes separate translation requests that share one session', async () => {
    let activeTranslations = 0;
    let maximumConcurrency = 0;
    const translate = vi.fn(async (text: string) => {
      activeTranslations += 1;
      maximumConcurrency = Math.max(maximumConcurrency, activeTranslations);
      await new Promise((resolve) => setTimeout(resolve, 1));
      activeTranslations -= 1;
      return `zh:${text}`;
    });
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () => new Response('', { status: 404 }));
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const first = provider.translate({ text: 'baby', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    await vi.waitFor(() => expect(translate).toHaveBeenCalledWith('baby'));
    const second = provider.translate({ text: 'hello', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    await Promise.all([first, second]);

    expect(maximumConcurrency).toBe(1);
  });

  it('drops stale queued selections instead of making the newest word wait for all of them', async () => {
    let releaseFirst: (() => void) | undefined;
    const firstGate = new Promise<void>((resolve) => {
      releaseFirst = resolve;
    });
    const translate = vi.fn(async (text: string) => {
      if (text === 'first') await firstGate;
      return `zh:${text}`;
    });
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () => new Response('', { status: 404 }));
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const first = provider.translate({ text: 'first', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    await vi.waitFor(() => expect(translate).toHaveBeenCalledWith('first'));
    const stale = provider.translate({ text: 'second', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    const newest = provider.translate({ text: 'third', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    releaseFirst?.();

    await expect(first).resolves.toMatchObject({ translatedText: 'zh:first' });
    await expect(stale).rejects.toThrow('replaced by a newer selection');
    await expect(newest).resolves.toMatchObject({ translatedText: 'zh:third' });
    expect(translate).not.toHaveBeenCalledWith('second');
  });

  it('caches a complete dictionary result so repeated manual clicks stay identical', async () => {
    const translate = vi.fn(async (text: string) => ({
      crouch: '蹲伏',
      'To bend low.': '弯低身体',
    })[text] ?? text);
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () => new Response(JSON.stringify([{
      word: 'crouch',
      phonetic: '/kɹaʊt͡ʃ/',
      meanings: [{ definitions: [{ definition: 'To bend low.' }] }],
    }])));
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const first = await provider.translate({ text: 'crouch', sourceLanguage: 'en', targetLanguage: 'zh-CN' });
    const second = await provider.translate({ text: 'crouch', sourceLanguage: 'en', targetLanguage: 'zh-CN' });

    expect(second).toMatchObject({ translatedText: first.translatedText, phonetic: first.phonetic });
    expect(dictionaryFetcher).toHaveBeenCalledOnce();
    expect(translatorApi.availability).toHaveBeenCalledOnce();
  });

  it('falls back to basic Chrome translation when the online dictionary fails', async () => {
    const translate = vi.fn(async (text: string) => `zh:${text}`);
    const translatorApi: BuiltInTranslatorApi = {
      availability: vi.fn(async () => 'available' as const),
      create: vi.fn(async () => ({ translate })),
    };
    const dictionaryFetcher = vi.fn(async () => {
      throw new TypeError('Network unavailable');
    });
    const provider = createChromeTranslationProvider(translatorApi, dictionaryFetcher);

    const result = await provider.translate({
      text: 'baby',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });

    expect(result).toMatchObject({
      translatedText: 'zh:baby',
      provider: 'chrome-local-dictionary-fallback',
    });
    expect(result.phonetic).toBeUndefined();
    expect(dictionaryFetcher).toHaveBeenCalledTimes(3);

    const repeated = await provider.translate({
      text: 'baby',
      sourceLanguage: 'en',
      targetLanguage: 'zh-CN',
    });
    expect(repeated.translatedText).toBe(result.translatedText);
    expect(dictionaryFetcher).toHaveBeenCalledTimes(6);
  });
});

