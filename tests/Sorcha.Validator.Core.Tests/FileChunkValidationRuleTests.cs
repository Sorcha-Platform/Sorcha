// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.TransactionHandler.Chunking;
using Sorcha.Validator.Core.Rules;
using Xunit;

namespace Sorcha.Validator.Core.Tests;

/// <summary>
/// Unit tests for FileChunkValidationRule.
/// Covers ValidateFileChunks, ValidateChunkMetadata, and ValidateOptionalFileField.
/// </summary>
public class FileChunkValidationRuleTests
{
    // -----------------------------------------------------------------------
    // Helper factory methods
    // -----------------------------------------------------------------------

    private static FileReference CreateValidFileReference(int chunkCount)
    {
        var ids = Enumerable.Range(0, chunkCount)
            .Select(i => $"tx-chunk-{i:D4}")
            .ToArray();

        return new FileReference
        {
            FileName = "document.pdf",
            ContentType = "application/pdf",
            Size = chunkCount * 1024L,
            Hash = "sha256:abcdef1234567890",
            Salt = "base64-salt-value",
            ChunkTransactionIds = ids,
            MasterKeyId = "tx-master-key"
        };
    }

    private static FileChunkMetadata CreateValidChunkMetadata(
        int index,
        int totalChunks,
        string fileHash = "sha256:abcdef1234567890",
        string contentType = "application/pdf",
        int chunkSize = 512 * 1024) // 512 KB — well under 4 MB
    {
        return new FileChunkMetadata
        {
            Type = FileChunkMetadata.MetadataType,
            FileHash = fileHash,
            ChunkIndex = index,
            TotalChunks = totalChunks,
            ContentType = contentType,
            ChunkSize = chunkSize
        };
    }

    private static FileSchemaExtension CreateSchemaExtension(
        string[] accept,
        string maxSize = "40MB",
        int maxChunks = 10)
    {
        return new FileSchemaExtension
        {
            Accept = accept,
            MaxSizePerFile = maxSize,
            MaxChunks = maxChunks
        };
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — success paths
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_AllChunksValid_ReturnsSuccess()
    {
        // Arrange
        var fileRef = CreateValidFileReference(3);
        var chunks = Enumerable.Range(0, 3)
            .Select(i => CreateValidChunkMetadata(i, 3))
            .ToList<FileChunkMetadata>();

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateFileChunks_SingleChunkValid_ReturnsSuccess()
    {
        // Arrange
        var fileRef = CreateValidFileReference(1);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 1, chunkSize: 1024)
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_001: missing / null chunk
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_MissingChunk_ReturnsFailure()
    {
        // Arrange — fileRef declares 3 chunks but list has null at position 1
        var fileRef = CreateValidFileReference(3);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 3),
            null!,
            CreateValidChunkMetadata(2, 3)
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.ChunkMissing);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_002: hash mismatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_HashMismatch_ReturnsFailure()
    {
        // Arrange — chunk 1 carries a different fileHash than the file reference
        var fileRef = CreateValidFileReference(3);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 3),
            CreateValidChunkMetadata(1, 3, fileHash: "sha256:WRONG_HASH"),
            CreateValidChunkMetadata(2, 3)
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.ChunkHashMismatch);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_003: non-contiguous / duplicate indices
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_NonContiguousIndices_ReturnsFailure()
    {
        // Arrange — indices 0 and 2 submitted; index 1 is absent (gap)
        var fileRef = CreateValidFileReference(2);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 2),
            CreateValidChunkMetadata(2, 2) // index 2 at position 1 → gap
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.ChunkIndexGap);
    }

    [Fact]
    public void ValidateFileChunks_DuplicateIndices_ReturnsFailure()
    {
        // Arrange — two chunks both claim index 0
        var fileRef = CreateValidFileReference(2);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 2),
            CreateValidChunkMetadata(0, 2) // duplicate index 0
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.ChunkIndexGap);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_004: oversized chunk
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_OversizedChunk_ReturnsFailure()
    {
        // Arrange — chunk 0 exceeds the 4 MB platform limit
        const int oversizedBytes = 5 * 1024 * 1024; // 5 MB
        var fileRef = CreateValidFileReference(1);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 1, chunkSize: oversizedBytes)
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.ChunkOversized);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_005: total size exceeds platform maximum (40 MB)
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_TotalSizeExceedsMax_ReturnsFailure()
    {
        // Arrange — 10 chunks each at 4 MB = 40 MB; adding +1 byte via an 11th chunk
        // would be FC_008. Instead: use 10 chunks each 4 MB + 1 byte (still ≤ 4 MB limit
        // per chunk but total > 40 MB). Actually 4 MB + 1 exceeds per-chunk limit too.
        // Use a different approach: 10 chunks, each exactly at max (4 MB) → total is 40 MB
        // which equals the limit. Push to 41 MB: use 10 chunks of exactly 4 MB + a schema
        // maxSizePerFile of "41MB" to isolate the platform limit (41 MB > 40 MB hard cap).
        // Simplest: 5 chunks × (8 MB + 1 byte) is over per-chunk too.
        // Best approach: 10 chunks at 4 MB each = exactly 40 MB, and the platform limit is
        // 40 MB (inclusive check is >). We need strictly over 40 MB using valid chunk sizes.
        // Each chunk must be ≤ 4 MB, so 10 chunks × 4 MB = exactly 40 MB (not over).
        // We cannot exceed 40 MB with 10 × 4 MB chunks without violating FC_004 or FC_008.
        // Use a schema extension with a smaller maxSizePerFile (e.g. "20MB") to trigger
        // FC_005 via the schema path — that is the realistic scenario.
        var fileRef = CreateValidFileReference(6);
        var schema = CreateSchemaExtension([], maxSize: "20MB", maxChunks: 10);

        // 6 chunks × 4 MB = 24 MB → exceeds schema limit of 20 MB
        const int chunkSize = 4 * 1024 * 1024; // exactly 4 MB (at limit, not over)
        var chunks = Enumerable.Range(0, 6)
            .Select(i => CreateValidChunkMetadata(i, 6, chunkSize: chunkSize))
            .ToList<FileChunkMetadata>();

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks, schema);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.TotalSizeExceeded);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_006: wrong MIME type
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_WrongMimeType_ReturnsFailure()
    {
        // Arrange — schema only permits PDF; chunk carries image/png
        var fileRef = CreateValidFileReference(1);
        var schema = CreateSchemaExtension(["application/pdf"]);
        var chunks = new List<FileChunkMetadata>
        {
            CreateValidChunkMetadata(0, 1, contentType: "image/png")
        };

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks, schema);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.MimeTypeMismatch);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_008: too many chunks
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_TooManyChunks_ReturnsFailure()
    {
        // Arrange — schema permits max 3 chunks but 5 are submitted
        var fileRef = CreateValidFileReference(5);
        var schema = CreateSchemaExtension([], maxSize: "40MB", maxChunks: 3);
        var chunks = Enumerable.Range(0, 5)
            .Select(i => CreateValidChunkMetadata(i, 5))
            .ToList<FileChunkMetadata>();

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks, schema);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == FileChunkValidationRule.TooManyChunks);
    }

    // -----------------------------------------------------------------------
    // ValidateFileChunks — FC_005 via schema maxSizePerFile
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateFileChunks_SchemaMaxSizeExceeded_ReturnsFailure()
    {
        // Arrange — 3 chunks × 1 MB = 3 MB; schema limit is 2 MB
        var fileRef = CreateValidFileReference(3);
        var schema = CreateSchemaExtension([], maxSize: "2MB", maxChunks: 10);
        const int chunkSize = 1 * 1024 * 1024; // 1 MB each
        var chunks = Enumerable.Range(0, 3)
            .Select(i => CreateValidChunkMetadata(i, 3, chunkSize: chunkSize))
            .ToList<FileChunkMetadata>();

        // Act
        var result = FileChunkValidationRule.ValidateFileChunks(fileRef, chunks, schema);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == FileChunkValidationRule.TotalSizeExceeded
            && e.Field == nameof(schema.MaxSizePerFile));
    }

    // -----------------------------------------------------------------------
    // ValidateChunkMetadata — success path
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateChunkMetadata_ValidChunk_ReturnsSuccess()
    {
        // Arrange
        var chunk = CreateValidChunkMetadata(2, 5);

        // Act
        var result = FileChunkValidationRule.ValidateChunkMetadata(chunk, expectedIndex: 2, expectedTotalChunks: 5);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // ValidateChunkMetadata — FC_009: wrong type discriminator
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateChunkMetadata_WrongType_ReturnsFailure()
    {
        // Arrange
        var chunk = CreateValidChunkMetadata(0, 1);
        chunk.Type = "action-payload"; // wrong discriminator

        // Act
        var result = FileChunkValidationRule.ValidateChunkMetadata(chunk, expectedIndex: 0, expectedTotalChunks: 1);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == FileChunkValidationRule.InvalidChunkMetadata
            && e.Field == nameof(FileChunkMetadata.Type));
    }

    // -----------------------------------------------------------------------
    // ValidateChunkMetadata — FC_009: wrong index
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateChunkMetadata_WrongIndex_ReturnsFailure()
    {
        // Arrange — chunk claims index 3 but is expected at position 1
        var chunk = CreateValidChunkMetadata(3, 5);

        // Act
        var result = FileChunkValidationRule.ValidateChunkMetadata(chunk, expectedIndex: 1, expectedTotalChunks: 5);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == FileChunkValidationRule.InvalidChunkMetadata
            && e.Field == nameof(FileChunkMetadata.ChunkIndex));
    }

    // -----------------------------------------------------------------------
    // ValidateChunkMetadata — FC_009: zero size
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateChunkMetadata_ZeroSize_ReturnsFailure()
    {
        // Arrange
        var chunk = CreateValidChunkMetadata(0, 1, chunkSize: 0);

        // Act
        var result = FileChunkValidationRule.ValidateChunkMetadata(chunk, expectedIndex: 0, expectedTotalChunks: 1);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == FileChunkValidationRule.InvalidChunkMetadata
            && e.Field == nameof(FileChunkMetadata.ChunkSize));
    }

    // -----------------------------------------------------------------------
    // ValidateOptionalFileField — success paths
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateOptionalFileField_NullValue_ReturnsSuccess()
    {
        // Act
        var result = FileChunkValidationRule.ValidateOptionalFileField(null);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateOptionalFileField_JsonNull_ReturnsSuccess()
    {
        // Arrange — deserialise a JSON null literal into a JsonElement
        var element = JsonSerializer.Deserialize<JsonElement>("null");

        // Act
        var result = FileChunkValidationRule.ValidateOptionalFileField(element);

        // Assert
        result.IsValid.Should().BeTrue();
        element.ValueKind.Should().Be(JsonValueKind.Null);
    }
}
