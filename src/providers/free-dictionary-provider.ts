type DictionaryEntry = {
  word?: string;
  phonetic?: string;
  phonetics?: Array<{ text?: string; audio?: string }>;
  meanings?: Array<{
    partOfSpeech?: string;
    definitions?: Array<{ definition?: string }>;
  }>;
};

type DatamuseEntry = {
  word?: string;
  defHeadword?: string;
  tags?: string[];
  defs?: string[];
};

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

export type EnglishDictionaryLookup = {
  headword: string;
  phonetic?: string;
  definitions: string[];
  senses: Array<{
    partOfSpeech?: string;
    definition: string;
  }>;
};

function lemmaCandidates(word: string): string[] {
  const value = word.toLocaleLowerCase('en');
  const candidates: string[] = [];
  const add = (candidate: string) => {
    if (candidate.length > 1 && candidate !== value && !candidates.includes(candidate)) {
      candidates.push(candidate);
    }
  };

  if (value.endsWith('ied') && value.length > 4) add(`${value.slice(0, -3)}y`);
  if (value.endsWith('ed') && value.length > 3) {
    const withoutEd = value.slice(0, -2);
    add(withoutEd);
    if (withoutEd.at(-1) === withoutEd.at(-2)) add(withoutEd.slice(0, -1));
    add(value.slice(0, -1));
  }
  if (value.endsWith('ing') && value.length > 5) {
    const withoutIng = value.slice(0, -3);
    add(withoutIng);
    if (withoutIng.at(-1) === withoutIng.at(-2)) add(withoutIng.slice(0, -1));
    add(`${withoutIng}e`);
  }
  if (value.endsWith('ies') && value.length > 4) add(`${value.slice(0, -3)}y`);
  if (value.endsWith('es') && value.length > 3) add(value.slice(0, -2));
  if (value.endsWith('s') && value.length > 2) add(value.slice(0, -1));

  return candidates;
}

async function fetchEntries(
  word: string,
  fetcher: FetchLike,
): Promise<DictionaryEntry[] | undefined> {
  const url = `https://api.dictionaryapi.dev/api/v2/entries/en/${encodeURIComponent(word)}`;
  let firstError: unknown;
  for (let attempt = 0; attempt < 2; attempt += 1) {
    try {
      const response = await fetcher(url);
      if (!response.ok) return undefined;
      const entries = (await response.json()) as DictionaryEntry[];
      return entries.length ? entries : undefined;
    } catch (error) {
      firstError ??= error;
      if (attempt === 0) {
        // One immediate retry absorbs brief network hand-offs without adding
        // the 600 ms fixed backoff used by the previous implementation.
        await new Promise((resolve) => setTimeout(resolve, 50));
      }
    }
  }
  throw firstError;
}

function phoneticFrom(entries?: DictionaryEntry[]): string | undefined {
  const entry = entries?.[0];
  const phonetics = entry?.phonetics ?? [];
  const candidates = [
    ...phonetics.filter((item) => /-us\.mp3(?:\?|$)/iu.test(item.audio ?? '')).map((item) => item.text),
    entry?.phonetic,
    ...phonetics.map((item) => item.text),
  ];
  for (const candidate of candidates) {
    const normalized = normalizePhonetic(candidate);
    if (normalized) return normalized;
  }
  return undefined;
}

/**
 * Keeps dictionary IPA intact while replacing syllabic-consonant combining
 * marks that are rendered as missing glyphs by common Windows UI fonts.
 * The replacements are phonemically equivalent, familiar IPA spellings.
 */
export function normalizePhonetic(value?: string): string | undefined {
  if (!value) return undefined;
  const arpabet = convertArpabetToIpa(value);
  if (resemblesArpabet(value) && !arpabet) return undefined;
  const normalized = (arpabet ?? value)
    .normalize('NFC')
    .replace(/l\u0329/gu, 'əl')
    .replace(/n\u0329/gu, 'ən')
    .replace(/m\u0329/gu, 'əm')
    // Use the learner-dictionary spelling used by Oxford/Longman-style
    // displays. U+0279 is valid IPA, but looks like a corrupted character to
    // many learners and differs from the pronunciation convention in Stage 1.
    .replace(/ɹ/gu, 'r')
    .replace(/ɚ/gu, 'ər')
    .replace(/\(j\)/gu, 'j')
    .replace(/\./gu, '')
    // Combining tie bars render as detached arcs in common Windows fonts.
    // tʃ/dʒ are the equivalent, familiar learner-dictionary spellings.
    .replace(/[\u035c\u0361]/gu, '')
    .trim();
  if (!normalized || /[\u0000-\u001f\u007f\ufffd\ue000-\uf8ff]/u.test(normalized)) {
    return undefined;
  }
  return normalized;
}

const ARPABET_VOWELS: Record<string, string> = {
  AA: 'ɑ', AE: 'æ', AH: 'ʌ', AO: 'ɔ', AW: 'aʊ', AY: 'aɪ',
  EH: 'ɛ', ER: 'ɝ', EY: 'eɪ', IH: 'ɪ', IY: 'i', OW: 'oʊ',
  OY: 'ɔɪ', UH: 'ʊ', UW: 'u',
};

const ARPABET_CONSONANTS: Record<string, string> = {
  B: 'b', CH: 'tʃ', D: 'd', DH: 'ð', F: 'f', G: 'ɡ', HH: 'h',
  JH: 'dʒ', K: 'k', L: 'l', M: 'm', N: 'n', NG: 'ŋ', P: 'p',
  R: 'r', S: 's', SH: 'ʃ', T: 't', TH: 'θ', V: 'v', W: 'w',
  Y: 'j', Z: 'z', ZH: 'ʒ',
};

function looksLikeArpabet(value: string): boolean {
  const unwrapped = value.trim().replace(/^[/[]|[\]/]$/gu, '');
  const tokens = unwrapped.split(/\s+/u);
  return tokens.length > 1 && tokens.every((token) => /^[A-Z]{1,3}[012]?$/u.test(token));
}

function resemblesArpabet(value: string): boolean {
  const tokens = value.trim().replace(/^[/[]|[\]/]$/gu, '').split(/\s+/u);
  return tokens.length > 1 && tokens.every((token) => /^[A-Z]+[012]?$/u.test(token));
}

/** Converts Datamuse/CMU ARPAbet into learner-friendly IPA. */
export function convertArpabetToIpa(value: string): string | undefined {
  if (!looksLikeArpabet(value)) return undefined;
  const tokens = value.trim().replace(/^[/[]|[\]/]$/gu, '').split(/\s+/u);
  let output = '';
  let pendingConsonants = '';
  for (const token of tokens) {
    const match = /^([A-Z]{1,3})([012]?)$/u.exec(token);
    if (!match) return undefined;
    const symbol = match[1]!;
    const stress = match[2] ?? '';
    const consonant = ARPABET_CONSONANTS[symbol];
    if (consonant) {
      pendingConsonants += consonant;
      continue;
    }
    let vowel = ARPABET_VOWELS[symbol];
    if (!vowel) return undefined;
    if (symbol === 'AH' && stress === '0') vowel = 'ə';
    if (symbol === 'ER' && stress === '0') vowel = 'ər';
    output += `${stress === '1' ? 'ˈ' : stress === '2' ? 'ˌ' : ''}${pendingConsonants}${vowel}`;
    pendingConsonants = '';
  }
  output += pendingConsonants;
  return output ? `/${output}/` : undefined;
}

function sensesFrom(entries?: DictionaryEntry[]): EnglishDictionaryLookup['senses'] {
  const senses: EnglishDictionaryLookup['senses'] = [];
  for (const meaning of entries?.[0]?.meanings ?? []) {
    const definition = meaning.definitions?.find((item) => item.definition)?.definition?.trim();
    if (definition && !senses.some((sense) => sense.definition === definition)) {
      senses.push({
        ...(meaning.partOfSpeech?.trim()
          ? { partOfSpeech: meaning.partOfSpeech.trim() }
          : {}),
        definition,
      });
    }
    if (senses.length === 3) break;
  }
  return senses;
}

function definitionsFrom(entries?: DictionaryEntry[]): string[] {
  return sensesFrom(entries).map((sense) => sense.definition);
}

const DATAMUSE_PARTS_OF_SPEECH: Record<string, string> = {
  n: 'noun',
  v: 'verb',
  adj: 'adjective',
  adv: 'adverb',
};

async function fetchDatamuseLookup(
  word: string,
  fetcher: FetchLike,
): Promise<EnglishDictionaryLookup | undefined> {
  const url =
    `https://api.datamuse.com/words?sp=${encodeURIComponent(word)}` +
    '&qe=sp&md=dpr&ipa=1&max=1';
  const response = await fetcher(url);
  if (!response.ok) return undefined;
  const entry = ((await response.json()) as DatamuseEntry[])[0];
  if (!entry?.word || entry.word.toLocaleLowerCase('en') !== word) return undefined;

  const tags = entry.tags ?? [];
  const phoneticTag = tags.find((tag) => tag.startsWith('pron:'));
  const taggedParts = tags
    .map((tag) => DATAMUSE_PARTS_OF_SPEECH[tag])
    .filter((value): value is string => Boolean(value));
  const senses: EnglishDictionaryLookup['senses'] = [];
  for (const rawDefinition of entry.defs ?? []) {
    const separator = rawDefinition.indexOf('\t');
    const code = separator >= 0 ? rawDefinition.slice(0, separator) : '';
    const definition = (separator >= 0
      ? rawDefinition.slice(separator + 1)
      : rawDefinition).trim();
    if (!definition || senses.some((sense) => sense.definition === definition)) continue;
    senses.push({
      ...(DATAMUSE_PARTS_OF_SPEECH[code] || taggedParts[0]
        ? { partOfSpeech: DATAMUSE_PARTS_OF_SPEECH[code] ?? taggedParts[0] }
        : {}),
      definition,
    });
    if (senses.length === 3) break;
  }

  const phonetic = normalizePhonetic(phoneticTag?.slice('pron:'.length));
  if (!phonetic && senses.length === 0 && taggedParts.length === 0) return undefined;
  return {
    headword: entry.defHeadword?.trim() || entry.word,
    ...(phonetic ? { phonetic } : {}),
    definitions: senses.map((sense) => sense.definition),
    senses,
  };
}

async function fetchPrimaryLookup(
  normalized: string,
  fetcher: FetchLike,
): Promise<EnglishDictionaryLookup | undefined> {
  const originalEntries = await fetchEntries(normalized, fetcher);
  let preferredEntries = originalEntries;
  let headword = originalEntries?.[0]?.word?.trim() || normalized;

  if (!originalEntries || definitionsFrom(originalEntries).length === 0) {
    for (const candidate of lemmaCandidates(normalized)) {
      const candidateEntries = await fetchEntries(candidate, fetcher);
      if (candidateEntries) {
        preferredEntries = candidateEntries;
        headword = candidateEntries[0]?.word?.trim() || candidate;
        break;
      }
    }
  }

  if (!preferredEntries) return undefined;
  const senses = sensesFrom(preferredEntries);
  return {
    headword,
    ...(phoneticFrom(originalEntries) || phoneticFrom(preferredEntries)
      ? { phonetic: phoneticFrom(originalEntries) || phoneticFrom(preferredEntries) }
      : {}),
    definitions: senses.map((sense) => sense.definition),
    senses,
  };
}

export async function fetchEnglishDictionaryLookup(
  word: string,
  fetcher: FetchLike = fetch,
): Promise<EnglishDictionaryLookup | undefined> {
  const normalized = word.trim().toLocaleLowerCase('en');
  let backupStarted = false;
  let backupTimer: ReturnType<typeof setTimeout> | undefined;

  return await new Promise((resolve, reject) => {
    let finished = false;
    let pending = 1;
    let lastError: unknown;
    const complete = (result?: EnglishDictionaryLookup) => {
      if (finished || !result) return false;
      finished = true;
      if (backupTimer) clearTimeout(backupTimer);
      resolve(result);
      return true;
    };
    const finishAttempt = (error?: unknown) => {
      if (error) lastError = error;
      pending -= 1;
      if (!finished && pending === 0) {
        finished = true;
        if (lastError) reject(lastError);
        else resolve(undefined);
      }
    };
    const startBackup = () => {
      if (finished || backupStarted) return;
      backupStarted = true;
      pending += 1;
      void fetchDatamuseLookup(normalized, fetcher).then(
        (result) => { if (!complete(result)) finishAttempt(); },
        (error) => finishAttempt(error),
      );
    };

    backupTimer = setTimeout(startBackup, 250);
    void fetchPrimaryLookup(normalized, fetcher).then(
      (result) => {
        if (complete(result)) return;
        startBackup();
        finishAttempt();
      },
      (error) => {
        startBackup();
        finishAttempt(error);
      },
    );
  });
}

export async function fetchEnglishPhonetic(
  word: string,
  fetcher: FetchLike = fetch,
): Promise<string | undefined> {
  return (await fetchEnglishDictionaryLookup(word, fetcher))?.phonetic;
}

