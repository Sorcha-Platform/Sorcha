// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sorcha.TransactionHandler.Chunking;

/// <summary>
/// Represents a single chunk of data read from a file stream.
/// </summary>
/// <param name="ChunkIndex">Zero-based index of this chunk within the file.</param>
/// <param name="Data">The chunk bytes. Contains exactly <paramref name="BytesRead"/> bytes of meaningful data.</param>
/// <param name="BytesRead">Number of bytes read into <paramref name="Data"/>. May be less than the configured chunk size for the final chunk.</param>
public record ChunkData(int ChunkIndex, byte[] Data, int BytesRead);

/// <summary>
/// Splits a <see cref="Stream"/> into fixed-size chunks suitable for streamed transaction processing.
/// Buffers are rented from <see cref="ArrayPool{T}"/> and cleared on return to avoid data leakage.
/// </summary>
public static class FileChunker
{
    /// <summary>Default chunk size: 4 MB.</summary>
    public const int DefaultChunkSize = 4 * 1024 * 1024;

    /// <summary>Maximum number of chunks permitted per file.</summary>
    public const int MaxChunksPerFile = 10;

    /// <summary>
    /// Asynchronously enumerates chunks of <paramref name="source"/> in order.
    /// Each <see cref="ChunkData"/> contains a fresh byte array sized to <paramref name="chunkSize"/>
    /// (or smaller for the final chunk) and the exact number of bytes read.
    /// </summary>
    /// <param name="source">Readable stream to chunk. Must support <see cref="Stream.CanRead"/>.</param>
    /// <param name="chunkSize">Maximum bytes per chunk. Defaults to <see cref="DefaultChunkSize"/> (4 MB).</param>
    /// <param name="ct">Cancellation token propagated to every read operation.</param>
    /// <returns>An async sequence of <see cref="ChunkData"/> records, yielded as each chunk is read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="source"/> is not readable, or <paramref name="chunkSize"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the stream produces more than <see cref="MaxChunksPerFile"/> chunks.</exception>
    public static async IAsyncEnumerable<ChunkData> ChunkFileAsync(
        Stream source,
        int chunkSize = DefaultChunkSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(source));

        if (chunkSize <= 0)
            throw new ArgumentException("Chunk size must be greater than zero.", nameof(chunkSize));

        var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            var chunkIndex = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var bytesRead = await ReadExactlyOrRemainderAsync(source, buffer, chunkSize, ct).ConfigureAwait(false);

                if (bytesRead == 0)
                    break;

                if (chunkIndex >= MaxChunksPerFile)
                    throw new InvalidOperationException(
                        $"Stream exceeds the maximum permitted chunk count of {MaxChunksPerFile}.");

                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);

                yield return new ChunkData(chunkIndex, chunk, bytesRead);

                chunkIndex++;

                if (bytesRead < chunkSize)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> bytes from <paramref name="stream"/> into <paramref name="buffer"/>,
    /// looping until the buffer is full or the stream reaches EOF.
    /// </summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="buffer">Destination buffer. Must be at least <paramref name="count"/> bytes long.</param>
    /// <param name="count">Maximum number of bytes to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total bytes read. May be less than <paramref name="count"/> if EOF was reached.</returns>
    private static async Task<int> ReadExactlyOrRemainderAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken ct)
    {
        var totalRead = 0;

        while (totalRead < count)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct)
                .ConfigureAwait(false);

            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }
}
