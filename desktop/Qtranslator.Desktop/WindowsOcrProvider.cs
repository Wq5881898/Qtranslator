using System.Diagnostics;
using System.IO;
using Qtranslator.Core;
using Windows.Graphics.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Qtranslator.Desktop;

public sealed class WindowsOcrProvider : IOcrProvider
{
    public string Id => "windows-local-ocr";
    public Task<ProviderHealth> CheckHealthAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var (engine, isEnglish) = CreateEngine();
        return Task.FromResult(engine is null
            ? new ProviderHealth(false, "Windows local OCR is unavailable.")
            : new ProviderHealth(true, isEnglish
                ? $"Windows English OCR ready ({engine.RecognizerLanguage.LanguageTag})."
                : $"Windows OCR fallback ready ({engine.RecognizerLanguage.LanguageTag}); install English OCR for best accuracy."));
    }

    public async Task<Qtranslator.Core.OcrResult> RecognizeAsync(Stream imageStream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var (engine, _) = CreateEngine();
        if (engine is null)
        {
            throw new InvalidOperationException("Windows local OCR is unavailable.");
        }
        var watch = Stopwatch.StartNew();
        using var randomAccess = new InMemoryRandomAccessStream();
        var output = randomAccess.AsStreamForWrite();
        await imageStream.CopyToAsync(output, token);
        await output.FlushAsync(token);
        randomAccess.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(randomAccess);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        watch.Stop();
        return new Qtranslator.Core.OcrResult(TextRules.Normalize(result.Text), watch.Elapsed, Id);
    }

    private static (OcrEngine? Engine, bool IsEnglish) CreateEngine()
    {
        foreach (var languageTag in new[] { "en-US", "en-GB" })
        {
            var language = new Language(languageTag);
            if (OcrEngine.IsLanguageSupported(language))
            {
                return (OcrEngine.TryCreateFromLanguage(language), true);
            }
        }

        return (OcrEngine.TryCreateFromUserProfileLanguages(), false);
    }
}
