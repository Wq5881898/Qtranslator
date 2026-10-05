using System.Diagnostics;
using System.IO;
using Tesseract;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public sealed class PackagedEnglishOcrProvider : IOcrProvider
{
    private readonly string _dataPath;
    private readonly bool _preserveAllText;

    public PackagedEnglishOcrProvider(string? dataPath = null, bool preserveAllText = false)
    {
        _dataPath = dataPath ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        _preserveAllText = preserveAllText;
    }

    public string Id => "packaged-bilingual-ocr";

    public Task<ProviderHealth> CheckHealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var englishModel = Path.Combine(_dataPath, "eng.traineddata");
        var chineseModel = Path.Combine(_dataPath, "chi_sim.traineddata");
        return Task.FromResult(File.Exists(englishModel) && File.Exists(chineseModel)
            ? new ProviderHealth(true, "Packaged English and Simplified Chinese OCR models are ready.")
            : new ProviderHealth(false, "A packaged bilingual OCR model is missing."));
    }

    public async Task<Qtranslator.Core.OcrResult> RecognizeAsync(Stream imageStream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var health = await CheckHealthAsync(token);
        if (!health.IsAvailable)
        {
            throw new InvalidOperationException(health.Message);
        }

        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, token);
        token.ThrowIfCancellationRequested();

        var watch = Stopwatch.StartNew();
        using var image = Pix.LoadFromMemory(buffer.ToArray());
        var english = Recognize(image, "eng");
        var bilingual = Recognize(image, "eng+chi_sim");
        var text = _preserveAllText ? bilingual.Text : TextRules.MergeEnglishFromBilingualOcr(english.Text, bilingual.Text);
        var confidence = bilingual.Text.Any(character => character is >= '\u3400' and <= '\u9FFF')
            ? bilingual.Confidence
            : english.Confidence;
        watch.Stop();

        return new Qtranslator.Core.OcrResult(text, watch.Elapsed, Id, confidence);
    }

    private (string Text, float Confidence) Recognize(Pix image, string languages)
    {
        using var engine = new TesseractEngine(_dataPath, languages, EngineMode.LstmOnly);
        engine.SetVariable("preserve_interword_spaces", "1");
        using var page = engine.Process(image, PageSegMode.Auto);
        return (TextRules.Normalize(page.GetText()), page.GetMeanConfidence());
    }
}
