export type TextKind = 'word' | 'sentence' | 'paragraph';

export type TranslationRequest = {
  text: string;
  sourceLanguage: 'en';
  targetLanguage: 'zh-CN';
};

export type TranslationResult = {
  originalText: string;
  translatedText: string;
  textKind: TextKind;
  provider: string;
  phonetic?: string;
  partsOfSpeech?: string[];
  meanings?: Array<{ partOfSpeech?: string; translatedText: string }>;
  alternatives?: string[];
};

export interface TranslationProvider {
  translate(request: TranslationRequest): Promise<TranslationResult>;
}

const SENTENCE_ENDING = /[.!?]$/u;
const WORDS_IN_SENTENCE = 12;

export function classifyText(text: string): TextKind {
  const words = text.trim().split(/\s+/u);

  if (words.length === 1 && !SENTENCE_ENDING.test(text)) {
    return 'word';
  }

  return words.length <= WORDS_IN_SENTENCE ? 'sentence' : 'paragraph';
}
