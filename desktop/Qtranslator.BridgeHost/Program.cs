using System.IO.Pipes;
using System.Collections.Concurrent;
using System.Text.Json;
using Qtranslator.Core;

var chromeInput = Console.OpenStandardInput();
var chromeOutput = Console.OpenStandardOutput();
using var lifetime = new CancellationTokenSource();
using var outputGate = new SemaphoreSlim(1, 1);
var pending = new ConcurrentDictionary<string, TaskCompletionSource<BridgeEnvelope>>();
var reader = ReadChromeAsync();

try
{
    while (!lifetime.IsCancellationRequested)
    {
        await using var pipe = new NamedPipeServerStream(
            BridgeEnvelope.PipeName, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync(lifetime.Token);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(90));
        string? requestId = null;
        try
        {
            var request = await NativeMessageFraming.ReadAsync(pipe, requestTimeout.Token);
            if (request is null) continue;
            requestId = request.RequestId;
            var completion = new TaskCompletionSource<BridgeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(requestId, completion)) throw new InvalidDataException("Duplicate request ID.");
            await WriteChromeAsync(request);
            var response = await completion.Task.WaitAsync(requestTimeout.Token);
            await NativeMessageFraming.WriteAsync(pipe, response, requestTimeout.Token);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            // A closed desktop window or timed-out request must not kill Chrome's port.
            Console.Error.WriteLine($"Desktop bridge request ended: {error.GetType().Name}");
        }
        finally
        {
            if (requestId is not null) pending.TryRemove(requestId, out _);
        }
    }
}
catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
finally
{
    lifetime.Cancel();
    await reader;
}

async Task WriteChromeAsync(BridgeEnvelope response)
{
    await outputGate.WaitAsync(lifetime.Token);
    try { await NativeMessageFraming.WriteAsync(chromeOutput, response, lifetime.Token); }
    finally { outputGate.Release(); }
}

async Task ReadChromeAsync()
{
    try
    {
        while (!lifetime.IsCancellationRequested)
        {
            var message = await NativeMessageFraming.ReadAsync(chromeInput, lifetime.Token);
            if (message is null) break;
            // Only response messages resolve desktop requests. Health probes and
            // favorites operations can arrive while a translation is pending.
            if (message.MessageType is "bridge.health.result" or "translation.result" or "bridge.error")
            {
                if (pending.TryGetValue(message.RequestId, out var completion)) completion.TrySetResult(message);
                continue;
            }
            var response = message.MessageType switch
            {
                "bridge.health" => BridgeEnvelope.Create("bridge.health.result", message.RequestId,
                    new { available = true, host = "qtranslator-bridge", protocolVersion = BridgeEnvelope.CurrentVersion }),
                "favorites.read" => BridgeEnvelope.Create("favorites.result", message.RequestId,
                    new { favorites = await SharedFavoriteStore.LoadAsync() }),
                "favorites.write" => await WriteFavoritesAsync(message),
                "favorites.patch" => await PatchFavoritesAsync(message),
                _ => BridgeEnvelope.Create("bridge.error", message.RequestId,
                    new { code = "unsupported_message", message = "Unsupported direct native-host request." })
            };
            await WriteChromeAsync(response);
        }
    }
    catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
    {
        Console.Error.WriteLine($"Chrome bridge connection ended: {error.GetType().Name}");
    }
    finally { lifetime.Cancel(); }
}

static async Task<BridgeEnvelope> PatchFavoritesAsync(BridgeEnvelope request)
{
    try
    {
        var upsert = request.Payload.TryGetProperty("upsert", out var upsertValue)
            ? upsertValue.Deserialize<List<FavoriteEntry>>() ?? []
            : [];
        var removeIds = request.Payload.TryGetProperty("removeIds", out var removeValue)
            ? removeValue.Deserialize<List<string>>() ?? []
            : [];
        if (upsert.Any(item => !SharedFavoriteStore.IsValid(item)))
        {
            throw new InvalidDataException("Favorites patch contains an invalid item.");
        }
        var favorites = await SharedFavoriteStore.PatchAsync(upsert, removeIds);
        return BridgeEnvelope.Create(
            "favorites.result",
            request.RequestId,
            new { favorites });
    }
    catch (Exception exception)
    {
        return BridgeEnvelope.Create(
            "bridge.error",
            request.RequestId,
            new { code = "favorites_patch_failed", message = exception.Message });
    }
}

static async Task<BridgeEnvelope> WriteFavoritesAsync(BridgeEnvelope request)
{
    try
    {
        if (!request.Payload.TryGetProperty("favorites", out var value))
        {
            throw new InvalidDataException("Favorites payload is missing.");
        }

        var favorites = value.Deserialize<List<FavoriteEntry>>() ?? [];
        if (favorites.Any(item => !SharedFavoriteStore.IsValid(item)))
        {
            throw new InvalidDataException("Favorites payload contains an invalid item.");
        }

        await SharedFavoriteStore.SaveAsync(favorites);
        return BridgeEnvelope.Create(
            "favorites.result",
            request.RequestId,
            new { favorites = await SharedFavoriteStore.LoadAsync() });
    }
    catch (Exception exception)
    {
        return BridgeEnvelope.Create(
            "bridge.error",
            request.RequestId,
            new { code = "favorites_write_failed", message = exception.Message });
    }
}

