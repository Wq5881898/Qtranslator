using System.IO.Pipes;
using System.IO;
using System.Text.Json;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public sealed class BrowserBridgeTranslationProvider : ITranslationProvider
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromSeconds(45);

    public string Id => "chrome-local-bridge";

    public async Task<ProviderHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAsync("bridge.health", new { }, ConnectTimeout, cancellationToken);
            return response.MessageType == "bridge.health.result"
                ? new ProviderHealth(true, "Chrome bridge is connected.")
                : new ProviderHealth(false, ReadError(response));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ProviderHealth(false, FriendlyBridgeError(exception));
        }
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var text = TextRules.ValidateAndNormalize(request.Text);
        try
        {
            var response = await SendAsync(
                "translation.request",
                new
                {
                    text,
                    sourceLanguage = request.SourceLanguage,
                    targetLanguage = request.TargetLanguage,
                },
                TranslationTimeout,
                cancellationToken);
            if (response.MessageType == "bridge.error")
            {
                throw new InvalidOperationException(ReadError(response));
            }
            if (response.MessageType != "translation.result")
            {
                throw new InvalidDataException($"Unexpected bridge response: {response.MessageType}");
            }

            var payload = response.Payload;
            return new TranslationResult(
                response.RequestId,
                payload.GetProperty("originalText").GetString() ?? text,
                payload.GetProperty("translatedText").GetString()
                    ?? throw new InvalidDataException("Chrome returned an empty translation."),
                ParseTextKind(payload.GetProperty("textKind").GetString()),
                payload.GetProperty("provider").GetString() ?? Id,
                payload.TryGetProperty("phonetic", out var phonetic)
                    ? phonetic.GetString()
                    : null,
                payload.TryGetProperty("partsOfSpeech", out var partsOfSpeech) &&
                    partsOfSpeech.ValueKind == JsonValueKind.Array
                    ? partsOfSpeech.EnumerateArray()
                        .Select(item => item.GetString())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToArray()
                    : null);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "Translation timed out. Keep Chrome open, then retry.");
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            exception is not InvalidOperationException)
        {
            throw new InvalidOperationException(FriendlyBridgeError(exception), exception);
        }
    }

    private static async Task<BridgeEnvelope> SendAsync(
        string messageType,
        object payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            BridgeEnvelope.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeoutSource.Token);
            var request = BridgeEnvelope.Create(
                messageType,
                Guid.NewGuid().ToString("N"),
                payload);
            await NativeMessageFraming.WriteAsync(pipe, request, timeoutSource.Token);
            return await NativeMessageFraming.ReadAsync(pipe, timeoutSource.Token)
                ?? throw new EndOfStreamException("The Chrome bridge closed without a response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }

    private static string ReadError(BridgeEnvelope response)
    {
        return response.Payload.TryGetProperty("message", out var message)
            ? message.GetString() ?? "Chrome bridge failed."
            : "Chrome bridge failed.";
    }

    private static TextKind ParseTextKind(string? textKind) => textKind switch
    {
        "word" => TextKind.Word,
        "sentence" => TextKind.Sentence,
        "paragraph" => TextKind.Paragraph,
        _ => TextKind.Sentence,
    };

    private static string FriendlyBridgeError(Exception exception) => exception switch
    {
        TimeoutException =>
            "Chrome bridge is not connected. Open Chrome with the Qtranslator extension enabled, then retry.",
        IOException =>
            "Chrome bridge disconnected. Keep Chrome open and retry.",
        JsonException =>
            "Chrome bridge returned invalid data. Reload the extension and retry.",
        _ => exception.Message,
    };
}
