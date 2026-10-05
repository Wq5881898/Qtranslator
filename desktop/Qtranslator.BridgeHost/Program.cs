using System.IO.Pipes;
using System.Text.Json;
using Qtranslator.Core;

var chromeInput = Console.OpenStandardInput();
var chromeOutput = Console.OpenStandardOutput();

while (true)
{
    using var pipe = new NamedPipeServerStream(
        BridgeEnvelope.PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);
    using var waitCancellation = new CancellationTokenSource();
    var chromeRead = NativeMessageFraming.ReadAsync(chromeInput, CancellationToken.None);
    var pipeWait = pipe.WaitForConnectionAsync(waitCancellation.Token);
    var completed = await Task.WhenAny(chromeRead, pipeWait);

    if (completed == chromeRead)
    {
        waitCancellation.Cancel();
        var directRequest = await chromeRead;
        if (directRequest is null)
        {
            break;
        }

        var directResponse = directRequest.MessageType switch
        {
            "bridge.health" => BridgeEnvelope.Create(
                "bridge.health.result",
                directRequest.RequestId,
                new
                {
                    available = true,
                    host = "qtranslator-bridge",
                    protocolVersion = BridgeEnvelope.CurrentVersion,
                }),
            "favorites.read" => BridgeEnvelope.Create(
                "favorites.result",
                directRequest.RequestId,
                new { favorites = await SharedFavoriteStore.LoadAsync() }),
            "favorites.write" => await WriteFavoritesAsync(directRequest),
            "favorites.patch" => await PatchFavoritesAsync(directRequest),
            _ => BridgeEnvelope.Create(
                "bridge.error",
                directRequest.RequestId,
                new
                {
                    code = "desktop_not_connected",
                    message = "Unsupported direct native-host request.",
                })
        };
        await NativeMessageFraming.WriteAsync(chromeOutput, directResponse, CancellationToken.None);
        continue;
    }

    await pipeWait;
    var desktopRequest = await NativeMessageFraming.ReadAsync(pipe, CancellationToken.None);
    if (desktopRequest is null)
    {
        continue;
    }

    await NativeMessageFraming.WriteAsync(chromeOutput, desktopRequest, CancellationToken.None);
    var extensionResponse = await chromeRead;
    if (extensionResponse is null)
    {
        break;
    }

    await NativeMessageFraming.WriteAsync(pipe, extensionResponse, CancellationToken.None);
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

