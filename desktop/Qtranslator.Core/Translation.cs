namespace Qtranslator.Core;

public enum TextKind { Word, Sentence, Paragraph }
public sealed record TranslationRequest(string RequestId, string Text, string SourceLanguage = "en", string TargetLanguage = "zh-CN");
public sealed record TranslationResult(
    string RequestId,
    string OriginalText,
    string TranslatedText,
    TextKind TextKind,
    string Provider,
    string? Phonetic = null,
    IReadOnlyList<string>? PartsOfSpeech = null);
public sealed record ProviderHealth(bool IsAvailable, string Message);

public static class TranslationDisplay
{
    public static string Format(TranslationResult result) =>
        result.TextKind == TextKind.Word && !string.IsNullOrWhiteSpace(result.Phonetic)
            ? $"{result.Phonetic}{Environment.NewLine}{result.TranslatedText}"
            : result.TranslatedText;
}

public interface ITranslationProvider
{
    string Id { get; }
    Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken);
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

public static class TextRules
{
    public const int MaximumLength = 5_000;
    public static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static string ValidateAndNormalize(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0) throw new ArgumentException("Enter or recognize some English text first.", nameof(text));
        if (normalized.Length > MaximumLength) throw new ArgumentException($"Text cannot exceed {MaximumLength} characters.", nameof(text));
        if (!normalized.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) throw new ArgumentException("The text must contain English letters.", nameof(text));
        return normalized;
    }

    public static EnglishTextAssessment AssessEnglishOcr(string text, float? confidence = null)
    {
        var normalized = CleanEnglishOcrArtifacts(ExtractEnglishOcrContent(text));
        if (normalized.Length == 0)
        {
            return new EnglishTextAssessment(false, "No text was recognized.");
        }

        if (confidence is < 0.45f)
        {
            return new EnglishTextAssessment(
                false,
                $"The OCR confidence was only {confidence.Value:P0}.");
        }

        var letters = normalized.Where(IsLatinLetter).ToArray();
        var isValidSingleLetterWord =
            normalized.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("i", StringComparison.OrdinalIgnoreCase);
        if (letters.Length < 2 && !isValidSingleLetterWord)
        {
            return new EnglishTextAssessment(false, "No reliable English letters were detected.");
        }

        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(IsLatinLetter).ToArray()))
            .Where(word => word.Length > 0)
            .ToArray();
        if (words.Length == 1 &&
            words[0].Length <= 3 &&
            words[0].All(character => char.IsLower(character)) &&
            !words[0].Any(IsEnglishVowel))
        {
            return new EnglishTextAssessment(false, "The result looks like a graphic mistaken for a short word.");
        }
        var suspiciousSingles = words.Count(word =>
            word.Length == 1 &&
            !word.Equals("a", StringComparison.OrdinalIgnoreCase) &&
            !word.Equals("i", StringComparison.OrdinalIgnoreCase));
        var containsMeaningfulWord = words.Any(word =>
            word.Length >= 2 ||
            word.Equals("a", StringComparison.OrdinalIgnoreCase) ||
            word.Equals("i", StringComparison.OrdinalIgnoreCase));
        if (!containsMeaningfulWord)
        {
            return new EnglishTextAssessment(false, "Only numbers, symbols, or isolated unit letters were detected.");
        }
        if (words.Length >= 4 && suspiciousSingles > Math.Max(1, words.Length / 3))
        {
            return new EnglishTextAssessment(false, "The result contains too many isolated letters.");
        }

        if (letters.Length >= 8)
        {
            var vowelCount = letters.Count(IsEnglishVowel);
            if ((double)vowelCount / letters.Length < 0.12)
            {
                return new EnglishTextAssessment(false, "The result does not look like English text.");
            }
        }

        return new EnglishTextAssessment(true, "Reliable English text detected.");
    }

    public static string CleanEnglishOcrArtifacts(string text)
    {
        var tokens = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (tokens.Count > 1 && IsLikelyLeadingGraphicArtifact(tokens[0]))
        {
            tokens.RemoveAt(0);
        }

        for (var index = 1; index < tokens.Count; index++)
        {
            if (tokens[index].Equals("s", StringComparison.OrdinalIgnoreCase) &&
                tokens[index - 1].Any(IsLatinLetter))
            {
                tokens[index - 1] = $"{tokens[index - 1].TrimEnd('\'')}'s";
                tokens.RemoveAt(index);
                index--;
                continue;
            }

            var previousLetters = new string(tokens[index - 1].Where(IsLatinLetter).ToArray());
            var currentLetters = new string(tokens[index].Where(IsLatinLetter).ToArray());
            var previousLooksTechnical =
                previousLetters.Skip(1).Any(char.IsUpper) ||
                previousLetters.Length >= 2 && previousLetters.All(char.IsUpper);
            if (previousLooksTechnical &&
                currentLetters.Length == 2 &&
                char.IsUpper(currentLetters[0]) &&
                currentLetters[1] == 'l')
            {
                tokens[index] = tokens[index].Replace("Cl", "CI", StringComparison.Ordinal);
            }
        }

        return string.Join(' ', tokens);
    }

    public static string ExtractEnglishOcrContent(string text)
    {
        var extracted = text.Select(character =>
        {
            var normalized = character switch
            {
                '\u2018' or '\u2019' or '\u02BC' or '\uFF07' => '\'',
                '\u201C' or '\u201D' => '"',
                _ => character
            };
            return IsLatinLetter(normalized) ||
                   char.IsDigit(normalized) ||
                   normalized is ' ' or '\r' or '\n' or '\t' ||
                   normalized is '.' or ',' or ':' or ';' or '!' or '?' or
                       '\'' or '"' or '-' or '_' or '/' or '\\' or
                       '(' or ')' or '[' or ']' or '{' or '}' or
                       '+' or '=' or '%' or '#' or '@' or '&' or '*'
                ? normalized
                : ' ';
        });
        return Normalize(new string(extracted.ToArray()));
    }

    public static string MergeEnglishFromBilingualOcr(string englishOnly, string bilingual)
    {
        if (!bilingual.Any(IsCjkCharacter))
        {
            return Normalize(englishOnly);
        }

        var englishTokens = Normalize(englishOnly)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bilingualTokens = ExtractEnglishOcrContent(bilingual)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var merged = new List<string>();
        var englishIndex = 0;

        foreach (var bilingualToken in bilingualTokens)
        {
            var matchIndex = -1;
            for (var candidate = englishIndex;
                 candidate < englishTokens.Length && candidate <= englishIndex + 2;
                 candidate++)
            {
                if (AreNearOcrTokens(englishTokens[candidate], bilingualToken))
                {
                    matchIndex = candidate;
                    break;
                }
            }

            if (matchIndex >= 0)
            {
                merged.Add(englishTokens[matchIndex]);
                englishIndex = matchIndex + 1;
            }
            else
            {
                merged.Add(bilingualToken);
            }
        }

        return Normalize(string.Join(' ', merged));
    }

    private static bool AreNearOcrTokens(string first, string second)
    {
        var left = new string(first.Where(char.IsLetterOrDigit).ToArray());
        var right = new string(second.Where(char.IsLetterOrDigit).ToArray());
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (left.Length != right.Length || left.Length == 0)
        {
            return false;
        }

        var differences = left
            .Zip(right)
            .Count(pair => char.ToUpperInvariant(pair.First) != char.ToUpperInvariant(pair.Second));
        return differences == 1;
    }

    private static bool IsCjkCharacter(char character) =>
        character is >= '\u3400' and <= '\u9FFF';

    private static bool IsLikelyLeadingGraphicArtifact(string token)
    {
        var letters = new string(token.Where(IsLatinLetter).ToArray());
        if (letters.Length == 0)
        {
            return !token.Any(char.IsDigit);
        }

        return letters.Length <= 3 &&
               letters.All(char.IsLower) &&
               !letters.Any(IsEnglishVowel);
    }

    private static bool IsEnglishVowel(char character) =>
        "aeiouyAEIOUY".Contains(character);

    private static bool IsLatinLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static TextKind Classify(string text)
    {
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1 && !".!?".Contains(words[0][^1])) return TextKind.Word;
        return words.Length <= 12 ? TextKind.Sentence : TextKind.Paragraph;
    }
}

public sealed record EnglishTextAssessment(bool IsReliable, string Message);

public sealed class MockTranslationProvider : ITranslationProvider
{
    public string Id => "mock";
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderHealth(true, "Mock provider is ready."));

    public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = TextRules.ValidateAndNormalize(request.Text);
        return Task.FromResult(new TranslationResult(request.RequestId, text, $"[模拟翻译] {text}", TextRules.Classify(text), Id));
    }
}
