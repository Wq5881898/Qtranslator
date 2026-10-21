using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Qtranslator.Core;

public sealed record FavoriteEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("originalText")] string OriginalText,
    [property: JsonPropertyName("translatedText")] string TranslatedText,
    [property: JsonPropertyName("firstFavoritedAt")] string FirstFavoritedAt,
    [property: JsonPropertyName("phonetic")] string? Phonetic = null);

public static class SharedFavoriteStore
{
    private const string SyncName = "Local\\Wq5881898.Qtranslator.Favorites";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string StorePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Qtranslator",
        "favorites.json");

    public static async Task<IReadOnlyList<FavoriteEntry>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StorePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(StorePath);
        var favorites = await JsonSerializer.DeserializeAsync<List<FavoriteEntry>>(
            stream,
            JsonOptions,
            cancellationToken);
        return favorites?.Where(IsValid).ToArray() ?? [];
    }

    public static async Task SaveAsync(
        IEnumerable<FavoriteEntry> favorites,
        CancellationToken cancellationToken = default)
    {
        var normalized = favorites
            .Where(IsValid)
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{StorePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, StorePath, true);
    }

    public static async Task<IReadOnlyList<FavoriteEntry>> PatchAsync(
        IEnumerable<FavoriteEntry> upsert,
        IEnumerable<string> removeIds,
        CancellationToken cancellationToken = default)
    {
        using var semaphore = new Semaphore(1, 1, SyncName);
        if (!semaphore.WaitOne(TimeSpan.FromSeconds(5)))
        {
            throw new IOException("The shared favorites library is busy. Retry.");
        }
        try
        {
            var removed = removeIds.ToHashSet(StringComparer.Ordinal);
            var merged = (await LoadAsync(cancellationToken))
                .Where(item => !removed.Contains(item.Id))
                .ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var item in upsert.Where(IsValid))
            {
                merged[item.Id] = item;
            }
            await SaveAsync(merged.Values, cancellationToken);
            return merged.Values.ToArray();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static bool IsValid(FavoriteEntry item) =>
        !string.IsNullOrWhiteSpace(item.Id) &&
        item.Kind is "word" or "sentence" &&
        !string.IsNullOrWhiteSpace(item.OriginalText) &&
        !string.IsNullOrWhiteSpace(item.TranslatedText) &&
        DateTimeOffset.TryParse(item.FirstFavoritedAt, out _);
}

public static class FavoritesCsv
{
    private static readonly string[] Headers =
        ["Type", "English", "Phonetic", "Chinese translation", "First saved"];

    public static string Serialize(IEnumerable<FavoriteEntry> favorites)
    {
        var rows = new List<string[]> { Headers };
        rows.AddRange(favorites.Select(item => new[]
        {
            item.Kind, item.OriginalText, item.Phonetic ?? "",
            item.TranslatedText, ExportDate(item.FirstFavoritedAt)
        }));
        return "\uFEFF" + string.Join(
            "\r\n",
            rows.Select(row => string.Join(",", row.Select(Escape)))) + "\r\n";
    }

    public static IReadOnlyList<FavoriteEntry> Parse(string text)
    {
        var rows = ParseRows(text.TrimStart('\uFEFF'));
        if (rows.Count == 0 || !rows[0].SequenceEqual(Headers))
        {
            throw new InvalidDataException("Choose a valid Qtranslator favorites CSV file.");
        }

        var result = new List<FavoriteEntry>();
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Count != Headers.Length)
            {
                throw new InvalidDataException($"CSV row {index + 1} has the wrong number of columns.");
            }
            var kind = row[0].Trim();
            var english = row[1].Trim();
            var phonetic = row[2].Trim();
            var chinese = row[3].Trim();
            var saved = row[4].Trim();
            var id = $"{kind}:{(kind == "word" ? english.ToLowerInvariant() : english)}";
            var item = new FavoriteEntry(id, kind, english, chinese, saved,
                string.IsNullOrWhiteSpace(phonetic) ? null : phonetic);
            if (!SharedFavoriteStore.IsValid(item))
            {
                throw new InvalidDataException($"CSV row {index + 1} is invalid.");
            }
            if (result.All(existing => existing.Id != item.Id)) result.Add(item);
        }
        return result;
    }

    private static string ExportDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToString("yyyy-MM-dd")
            : value.Length >= 10 ? value[..10] : value;

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted && character == '"' && index + 1 < text.Length && text[index + 1] == '"')
            {
                cell.Append('"');
                index++;
            }
            else if (character == '"') quoted = !quoted;
            else if (character == ',' && !quoted)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (character == '\n' && !quoted)
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                if (row.Any(value => value.Length > 0)) rows.Add(row);
                row = [];
            }
            else cell.Append(character);
        }
        if (quoted) throw new InvalidDataException("The CSV file contains an unfinished quoted value.");
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            rows.Add(row);
        }
        return rows;
    }
}

