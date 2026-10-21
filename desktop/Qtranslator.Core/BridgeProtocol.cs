using System.Buffers.Binary;
using System.Text.Json;

namespace Qtranslator.Core;

public sealed record BridgeEnvelope(string ProtocolVersion, string MessageType, string RequestId, DateTimeOffset SentAt, JsonElement Payload)
{
    public const string CurrentVersion = "1.0";
    public const int MaximumNativeMessageBytes = 1_048_576;
    public const string DefaultPipeName = "wq5881898.qtranslator";
    public const string PipeNameEnvironmentVariable = "QTRANSLATOR_BRIDGE_PIPE_NAME";
    public static string PipeName =>
        Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable) is { Length: > 0 } configured
            ? configured
            : DefaultPipeName;
    public static BridgeEnvelope Create<T>(string type, string requestId, T payload) =>
        new(CurrentVersion, type, requestId, DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(payload));
}

public static class NativeMessageFraming
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static async Task WriteAsync(Stream stream, BridgeEnvelope envelope, CancellationToken token)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        if (body.Length > BridgeEnvelope.MaximumNativeMessageBytes) throw new InvalidDataException("Native message exceeds 1 MiB.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    public static async Task<BridgeEnvelope?> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEndAsync(stream, header, token)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > BridgeEnvelope.MaximumNativeMessageBytes) throw new InvalidDataException("Invalid native message length.");
        var body = new byte[length];
        if (!await ReadExactlyOrEndAsync(stream, body, token)) throw new EndOfStreamException("Incomplete native message.");
        var envelope = JsonSerializer.Deserialize<BridgeEnvelope>(body, Options) ?? throw new InvalidDataException("Empty native message.");
        if (envelope.ProtocolVersion != BridgeEnvelope.CurrentVersion) throw new InvalidDataException($"Unsupported protocol: {envelope.ProtocolVersion}");
        return envelope;
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken token)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[total..], token);
            if (count == 0) return total == 0 ? false : throw new EndOfStreamException();
            total += count;
        }
        return true;
    }
}
