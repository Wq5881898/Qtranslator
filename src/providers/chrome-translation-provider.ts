import {
  validateEnglishTranslationInput,
  withTimeout,
} from "../core/translation-guard";
import {
  classifyText,
  type TranslationProvider,
  type TranslationRequest,
  type TranslationResult,
} from "./translation-provider";
import {
  fetchEnglishDictionaryLookup,
  type FetchLike,
} from "./free-dictionary-provider";

type Availability =
  | "available"
  | "downloadable"
  | "downloading"
  | "unavailable";

type BuiltInTranslatorSession = {
  translate(text: string): Promise<string>;
  destroy?(): void;
};

export type BuiltInTranslatorApi = {
  availability(options: {
    sourceLanguage: string;
    targetLanguage: string;
  }): Promise<Availability>;
  create(options: {
    sourceLanguage: string;
    targetLanguage: string;
  }): Promise<BuiltInTranslatorSession>;
};

function defaultApi(): BuiltInTranslatorApi | undefined {
  return (
    globalThis as typeof globalThis & { Translator?: BuiltInTranslatorApi }
  ).Translator;
}

export function createChromeTranslationProvider(
  suppliedApi?: BuiltInTranslatorApi,
  dictionaryFetcher: FetchLike = fetch,
): TranslationProvider {
  let sessionPromise: Promise<BuiltInTranslatorSession> | undefined;
  let translationQueue: Promise<void> = Promise.resolve();
  let latestQueuedRequest = 0;
  const dictionaryCache = new Map<string, Promise<Awaited<ReturnType<typeof fetchEnglishDictionaryLookup>>>>();

  function lookupWord(text: string) {
    const cacheKey = text.trim().toLocaleLowerCase('en');
    let lookup = dictionaryCache.get(cacheKey);
    if (!lookup) {
      lookup = fetchEnglishDictionaryLookup(cacheKey, dictionaryFetcher);
      dictionaryCache.set(cacheKey, lookup);
      lookup.catch(() => dictionaryCache.delete(cacheKey));
    }
    return lookup;
  }

  async function discardSession(): Promise<void> {
    const staleSession = sessionPromise;
    sessionPromise = undefined;
    if (!staleSession) return;

    try {
      const session = await staleSession;
      session.destroy?.();
    } catch {
      // A rejected create promise has no live session to destroy.
    }
  }

  async function getSession(): Promise<BuiltInTranslatorSession> {
    // Reuse the live local model immediately. Asking Chrome for availability
    // before every word can itself stall while the component service wakes.
    if (sessionPromise) return sessionPromise;

    const api = suppliedApi ?? defaultApi();

    if (!api) {
      throw new Error(
        "Local translation is unavailable. Update desktop Chrome to version 138 or later.",
      );
    }

    const availability = await api.availability({
      sourceLanguage: "en",
      targetLanguage: "zh",
    });

    if (availability === "unavailable") {
      throw new Error(
        "Chrome does not support the English-to-Chinese language pack on this device.",
      );
    }

    try {
      sessionPromise ??= api.create({
        sourceLanguage: "en",
        targetLanguage: "zh",
      });
      return await sessionPromise;
    } catch (error) {
      sessionPromise = undefined;

      if (error instanceof DOMException && error.name === "NotAllowedError") {
        throw new Error(
          "Chrome needs a browser click to prepare the local language pack. Open the Qtranslator side panel, click Run local translation check once, then retry in Windows.",
        );
      }

      throw error;
    }
  }

  async function translateSerially(
    session: BuiltInTranslatorSession,
    inputs: string[],
  ): Promise<string[]> {
    const translations: string[] = [];
    for (const input of inputs) {
      // Chrome Translator sessions are stateful and may fail when one session is
      // asked to translate several dictionary senses concurrently.
      translations.push(await withTimeout(session.translate(input)));
    }
    return translations;
  }

  async function translateWithFreshSessionRetry(
    inputs: string[],
  ): Promise<string[]> {
    const session = await withTimeout(getSession());
    try {
      return await translateSerially(session, inputs);
    } catch (firstError) {
      await discardSession();
      console.warn(
        "[chrome-translation] Local session failed; retrying with a fresh session.",
        {
          error:
            firstError instanceof Error
              ? firstError.message
              : String(firstError),
        },
      );

      try {
        const freshSession = await withTimeout(getSession());
        return await translateSerially(freshSession, inputs);
      } catch (retryError) {
        await discardSession();
        throw new Error(
          "Chrome local translation failed after the session was reset. Reload the extension or restart Chrome, then retry.",
          { cause: retryError },
        );
      }
    }
  }

  async function performTranslation(
    request: TranslationRequest,
  ): Promise<TranslationResult> {
    const text = validateEnglishTranslationInput(request.text);
    const textKind = classifyText(text);
    let dictionaryUnavailable = false;
    let dictionaryResult: Awaited<ReturnType<typeof fetchEnglishDictionaryLookup>>;
    if (textKind === "word") {
      const dictionaryKey = text.toLocaleLowerCase('en');
      try {
        dictionaryResult = await withTimeout(
          lookupWord(text),
          1_200,
          "Dictionary lookup timed out.",
        );
      } catch (error) {
        dictionaryUnavailable = true;
        dictionaryCache.delete(dictionaryKey);
        console.warn(
          "[chrome-translation] Dictionary unavailable; using basic local translation.",
          { error: error instanceof Error ? error.message : String(error) },
        );
      }
    }
    // A single combined call is materially faster than translating the
    // headword and every definition one by one. Newlines are retained by the
    // local Translator API and make the result readable even if punctuation
    // is normalized by the model.
    const translationInputs = dictionaryResult?.definitions.length
      ? [[dictionaryResult.headword, ...dictionaryResult.definitions]
          .map((value, index) => `[${index}] ${value}`)
          .join('\n')]
      : [text];
    const translatedSenses =
      await translateWithFreshSessionRetry(translationInputs);
    const combinedTranslation = translatedSenses[0] ?? '';
    const indexedTranslations = new Map<number, string>();
    for (const line of combinedTranslation.split(/\r?\n/u)) {
      const match = /^\s*\[(\d+)\]\s*(.+)$/u.exec(line);
      if (match) indexedTranslations.set(Number(match[1]!), match[2]!.trim());
    }
    const alternatives = combinedTranslation
      .split(/\r?\n|；/u)
      .map((value) => value.trim().replace(/^\d+[.)、:]\s*/u, ''))
      .filter(
        (value, index, values) =>
          value.length > 0 && values.indexOf(value) === index,
      )
      .slice(0, 4);
    const directTranslation = indexedTranslations.get(0);
    const groupedMeanings = new Map<string, string[]>();
    dictionaryResult?.senses.forEach((sense, index) => {
      const translated = indexedTranslations.get(index + 1);
      if (!translated) return;
      const partOfSpeech = sense.partOfSpeech || 'meaning';
      const values = groupedMeanings.get(partOfSpeech) ?? [];
      if (!values.includes(translated)) values.push(translated);
      groupedMeanings.set(partOfSpeech, values);
    });
    const meanings = [...groupedMeanings].map(([partOfSpeech, values], index) => ({
      partOfSpeech,
      translatedText: [groupedMeanings.size === 1 && index === 0 ? directTranslation : undefined, ...values]
        .filter((value): value is string => Boolean(value))
        .filter((value, valueIndex, all) => all.indexOf(value) === valueIndex)
        .join('；'),
    })).filter((meaning) => meaning.translatedText);
    const normalizedTranslation = meanings.length
      ? meanings.map((meaning) => `${meaning.partOfSpeech}：${meaning.translatedText}`).join('\n')
      : alternatives.join("；");

    if (!normalizedTranslation) {
      throw new Error("Chrome returned an empty translation. Try again.");
    }

    return {
      originalText: text,
      translatedText: normalizedTranslation,
      textKind,
      provider: dictionaryUnavailable
        ? "chrome-local-dictionary-fallback"
        : "chrome-local",
      ...(dictionaryResult?.phonetic
        ? { phonetic: dictionaryResult.phonetic }
        : {}),
      ...(dictionaryResult?.senses.length
        ? {
            partsOfSpeech: [...new Set(
              dictionaryResult.senses
                .map((sense) => sense.partOfSpeech)
                .filter((value): value is string => Boolean(value)),
            )],
          }
        : {}),
      ...(meanings.length ? { meanings } : {}),
      ...(alternatives.length > 1 ? { alternatives } : {}),
    };
  }

  return {
    translate(request: TranslationRequest): Promise<TranslationResult> {
      const queuedRequest = ++latestQueuedRequest;
      const operation = translationQueue.then(() => {
        if (queuedRequest !== latestQueuedRequest) {
          throw new Error('Translation was replaced by a newer selection.');
        }
        return performTranslation(request);
      });
      translationQueue = operation.then(
        () => undefined,
        () => undefined,
      );
      return operation;
    },
  };
}

export const chromeTranslationProvider = createChromeTranslationProvider();
