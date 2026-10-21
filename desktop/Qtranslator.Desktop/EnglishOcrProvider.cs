using System.IO;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public sealed class EnglishOcrProvider : IOcrProvider
{
    private readonly IOcrProvider _primary;
    private readonly IOcrProvider _fallback;

    public EnglishOcrProvider(IOcrProvider? primary = null, IOcrProvider? fallback = null)
    {
        _primary = primary ?? new PackagedEnglishOcrProvider();
        _fallback = fallback ?? new WindowsOcrProvider();
    }

    public string Id => "english-ocr";

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken token)
    {
        var primaryHealth = await _primary.CheckHealthAsync(token);
        if (primaryHealth.IsAvailable)
        {
            return primaryHealth;
        }

        var fallbackHealth = await _fallback.CheckHealthAsync(token);
        return fallbackHealth.IsAvailable
            ? new ProviderHealth(true, $"{primaryHealth.Message} Using Windows OCR as fallback.")
            : new ProviderHealth(false, $"{primaryHealth.Message} {fallbackHealth.Message}");
    }

    public async Task<Qtranslator.Core.OcrResult> RecognizeAsync(Stream imageStream, CancellationToken token)
    {
        var startPosition = imageStream.CanSeek ? imageStream.Position : 0;
        try
        {
            return await _primary.RecognizeAsync(imageStream, token);
        }
        catch (Exception primaryError) when (primaryError is not OperationCanceledException)
        {
            if (!imageStream.CanSeek)
            {
                throw new InvalidOperationException(
                    $"Packaged English OCR failed and the image cannot be retried: {primaryError.Message}",
                    primaryError);
            }

            imageStream.Position = startPosition;
            try
            {
                return await _fallback.RecognizeAsync(imageStream, token);
            }
            catch (Exception fallbackError) when (fallbackError is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"Packaged English OCR failed: {primaryError.Message} Windows OCR fallback also failed: {fallbackError.Message}",
                    new AggregateException(primaryError, fallbackError));
            }
        }
    }
}
