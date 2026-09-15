using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WindowsMemoryReaper.Services;

/// <summary>Tray → worker: request a cleanup cycle.</summary>
public sealed class CleanRequest
{
    public string RamMapPath { get; set; } = string.Empty;

    /// <summary>Operations to perform in canonical order. Null = fallback to all five.</summary>
    public string[]? Operations { get; set; }
}

/// <summary>Worker → tray: outcome of a cleanup cycle.</summary>
public sealed class CleanReply
{
    public string Kind { get; set; } = string.Empty;
    public int CompletedOperations { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Source-generated JSON context for pipe messages (AOT-friendly).</summary>
[JsonSerializable(typeof(CleanRequest))]
[JsonSerializable(typeof(CleanReply))]
internal sealed partial class PipeJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Length-prefixed UTF-8 JSON framing over the named pipe between the tray
/// process and the elevated cleanup worker.
/// </summary>
public static class PipeProtocol
{
    public const int MaxFrameBytes = 64 * 1024;

    public static byte[] Serialize<T>(T message, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.SerializeToUtf8Bytes(message, jsonTypeInfo);

    public static T? Deserialize<T>(byte[] bytes, JsonTypeInfo<T> jsonTypeInfo)
        => JsonSerializer.Deserialize(bytes, jsonTypeInfo);

    public static async Task WriteChunkAsync(Stream stream, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.Length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Frame of {payload.Length} bytes exceeds limit {MaxFrameBytes}.");
        }

        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, payload.Length);

        await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadChunkAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new InvalidDataException($"Invalid frame length {length}.");
        }

        var payload = new byte[length];
        if (length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return payload;
    }
}