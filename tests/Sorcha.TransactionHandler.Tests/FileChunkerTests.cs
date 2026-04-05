// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.TransactionHandler.Chunking;

namespace Sorcha.TransactionHandler.Tests;

public class FileChunkerTests
{
    // Use a small chunk size for most tests so they run fast without allocating megabytes.
    private const int SmallChunk = 1024; // 1 KB

    // ---------------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="MemoryStream"/> pre-populated with <paramref name="sizeInBytes"/>
    /// bytes. Each byte value is <c>(byte)(index % 256)</c> so data integrity checks are possible.
    /// The stream position is reset to 0 before being returned.
    /// </summary>
    private static MemoryStream CreateTestStream(int sizeInBytes)
    {
        var data = new byte[sizeInBytes];
        for (var i = 0; i < sizeInBytes; i++)
            data[i] = (byte)(i % 256);

        return new MemoryStream(data);
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ChunkFileAsync_SmallFile_ReturnsSingleChunk()
    {
        // Arrange — 1 KB stream with a 4 KB chunk size → must fit in one chunk.
        const int dataSize = SmallChunk; // 1 KB
        await using var stream = CreateTestStream(dataSize);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: SmallChunk * 4).ToListAsync();

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].BytesRead.Should().Be(dataSize);
        chunks[0].Data.Should().HaveCount(dataSize);
    }

    [Fact]
    public async Task ChunkFileAsync_ExactChunkSize_ReturnsSingleChunk()
    {
        // Arrange — stream is exactly the chunk size → 1 chunk, no remainder.
        const int chunkSize = SmallChunk;
        await using var stream = CreateTestStream(chunkSize);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: chunkSize).ToListAsync();

        // Assert
        chunks.Should().HaveCount(1);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].BytesRead.Should().Be(chunkSize);
    }

    [Fact]
    public async Task ChunkFileAsync_LargerThanChunk_ReturnsMultipleChunks()
    {
        // Arrange — 10 KB stream, 4 KB chunks → chunks of 4 KB, 4 KB, 2 KB.
        const int chunkSize = SmallChunk * 4; // 4 KB
        const int dataSize = SmallChunk * 10; // 10 KB
        await using var stream = CreateTestStream(dataSize);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: chunkSize).ToListAsync();

        // Assert
        chunks.Should().HaveCount(3);

        chunks[0].ChunkIndex.Should().Be(0);
        chunks[0].BytesRead.Should().Be(chunkSize);

        chunks[1].ChunkIndex.Should().Be(1);
        chunks[1].BytesRead.Should().Be(chunkSize);

        chunks[2].ChunkIndex.Should().Be(2);
        chunks[2].BytesRead.Should().Be(SmallChunk * 2); // 2 KB remainder
    }

    [Fact]
    public async Task ChunkFileAsync_EmptyStream_ReturnsNoChunks()
    {
        // Arrange
        await using var stream = CreateTestStream(0);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: SmallChunk).ToListAsync();

        // Assert
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task ChunkFileAsync_ExactMultiple_ReturnsCorrectChunks()
    {
        // Arrange — 8 KB stream, 4 KB chunks → exactly 2 chunks of 4 KB each.
        const int chunkSize = SmallChunk * 4; // 4 KB
        const int dataSize = SmallChunk * 8;  // 8 KB
        await using var stream = CreateTestStream(dataSize);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: chunkSize).ToListAsync();

        // Assert
        chunks.Should().HaveCount(2);
        chunks[0].BytesRead.Should().Be(chunkSize);
        chunks[1].BytesRead.Should().Be(chunkSize);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[1].ChunkIndex.Should().Be(1);
    }

    [Fact]
    public async Task ChunkFileAsync_CustomChunkSize_Respected()
    {
        // Arrange — use an irregular chunk size of 300 bytes over 1000 bytes of data.
        // Expected: chunks of 300, 300, 300, 100 bytes.
        const int chunkSize = 300;
        const int dataSize = 1000;
        await using var stream = CreateTestStream(dataSize);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: chunkSize).ToListAsync();

        // Assert
        chunks.Should().HaveCount(4);
        chunks[0].BytesRead.Should().Be(300);
        chunks[1].BytesRead.Should().Be(300);
        chunks[2].BytesRead.Should().Be(300);
        chunks[3].BytesRead.Should().Be(100);

        // Indices must be sequential.
        chunks.Select(c => c.ChunkIndex).Should().BeEquivalentTo([0, 1, 2, 3], opts => opts.WithStrictOrdering());
    }

    [Fact]
    public async Task ChunkFileAsync_NullStream_ThrowsArgumentNullException()
    {
        // Arrange
        Stream? nullStream = null;

        // Act
        var act = async () =>
            await FileChunker.ChunkFileAsync(nullStream!, chunkSize: SmallChunk).ToListAsync();

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("source");
    }

    [Fact]
    public async Task ChunkFileAsync_PreservesDataIntegrity()
    {
        // Arrange — 5 KB stream split into 2 KB chunks → 3 chunks.
        // Reassemble and compare byte-for-byte.
        const int chunkSize = SmallChunk * 2; // 2 KB
        const int dataSize = SmallChunk * 5;  // 5 KB
        await using var stream = CreateTestStream(dataSize);

        var expectedBytes = new byte[dataSize];
        for (var i = 0; i < dataSize; i++)
            expectedBytes[i] = (byte)(i % 256);

        // Act
        var chunks = await FileChunker.ChunkFileAsync(stream, chunkSize: chunkSize).ToListAsync();

        // Reassemble
        using var reassembled = new MemoryStream();
        foreach (var chunk in chunks)
            await reassembled.WriteAsync(chunk.Data.AsMemory(0, chunk.BytesRead));

        // Assert
        reassembled.ToArray().Should().Equal(expectedBytes);
    }
}

// ---------------------------------------------------------------------------
// Helper extension — collects IAsyncEnumerable<T> into a List<T>.
// ---------------------------------------------------------------------------
file static class AsyncEnumerableExtensions
{
    internal static async Task<List<T>> ToListAsync<T>(
        this IAsyncEnumerable<T> source,
        CancellationToken ct = default)
    {
        var list = new List<T>();
        await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
            list.Add(item);
        return list;
    }
}
