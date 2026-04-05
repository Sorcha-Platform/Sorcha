// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models;
using Sorcha.TransactionHandler.Chunking;
using Sorcha.Validator.Core.Models;

namespace Sorcha.Validator.Core.Rules;

/// <summary>
/// Validates that file-bearing action transactions have valid chunk references.
/// Used by the validator before sealing a docket.
/// Stateless and enclave-safe: no network or database access.
/// </summary>
public static class FileChunkValidationRule
{
    /// <summary>Chunk count does not match the number of chunk transaction IDs on the file reference.</summary>
    public const string ChunkMissing = "FC_001";

    /// <summary>A chunk's <c>fileHash</c> does not match the file reference's <c>hash</c>.</summary>
    public const string ChunkHashMismatch = "FC_002";

    /// <summary>Chunk indices are not contiguous from 0 to N-1 (gap or duplicate detected).</summary>
    public const string ChunkIndexGap = "FC_003";

    /// <summary>A single chunk's size exceeds <see cref="FileSchemaExtension.DefaultChunkSizeBytes"/> (4 MB).</summary>
    public const string ChunkOversized = "FC_004";

    /// <summary>Total file size across all chunks exceeds the platform maximum of 40 MB.</summary>
    public const string TotalSizeExceeded = "FC_005";

    /// <summary>A chunk's <c>contentType</c> does not match the MIME types permitted by the schema extension.</summary>
    public const string MimeTypeMismatch = "FC_006";

    /// <summary>
    /// Reserved for future use: a chunk transaction has already been sealed into a docket and cannot be reused.
    /// FC_007 is not currently emitted by <see cref="ValidateFileChunks"/> because sealed-status information
    /// is unavailable to the stateless validator at the time of validation. It is defined here so that
    /// tooling and documentation remain consistent when sealed-status becomes queryable.
    /// </summary>
    public const string ChunkAlreadySealed = "FC_007";

    /// <summary>The number of chunks exceeds the maximum allowed by the schema extension or platform limit.</summary>
    public const string TooManyChunks = "FC_008";

    /// <summary>A chunk's metadata fields are structurally invalid (wrong type, bad index, zero size, missing hash).</summary>
    public const string InvalidChunkMetadata = "FC_009";

    /// <summary>
    /// Validates that the supplied chunk metadata collection is consistent with the given file reference
    /// and optional schema extension constraints.
    /// </summary>
    /// <remarks>
    /// All errors are accumulated before returning so the caller receives a complete picture of every
    /// violation in a single pass. Checks performed (in order):
    /// <list type="bullet">
    ///   <item>Chunk count matches <c>fileRef.ChunkTransactionIds.Length</c>.</item>
    ///   <item>Each entry in <paramref name="chunks"/> is non-null.</item>
    ///   <item>Every chunk's <c>FileHash</c> equals <c>fileRef.Hash</c>.</item>
    ///   <item>Chunk indices form a contiguous, zero-based sequence with no gaps or duplicates.</item>
    ///   <item>Each chunk size is within the 4 MB platform default.</item>
    ///   <item>Aggregate size does not exceed the 40 MB platform maximum.</item>
    ///   <item>When <paramref name="schemaExtension"/> is supplied: per-file size limit, max-chunks
    ///         limit, and MIME-type allowlist are enforced.</item>
    /// </list>
    /// </remarks>
    /// <param name="fileRef">The file reference from the action payload.</param>
    /// <param name="chunks">
    /// Chunk metadata in submission order. May contain <see langword="null"/> entries for missing chunks;
    /// these are reported as <see cref="ChunkMissing"/> errors.
    /// </param>
    /// <param name="schemaExtension">
    /// Optional schema-level constraints parsed from the blueprint's <c>x-file</c> extension.
    /// When <see langword="null"/> only platform-level limits are applied.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success()"/> when all checks pass;
    /// <see cref="ValidationResult.Failure(ValidationError[])"/> with every violation otherwise.
    /// </returns>
    public static ValidationResult ValidateFileChunks(
        FileReference fileRef,
        IReadOnlyList<FileChunkMetadata> chunks,
        FileSchemaExtension? schemaExtension = null)
    {
        var errors = new List<ValidationError>();
        int expectedCount = fileRef.ChunkTransactionIds.Length;

        // 1. Chunk count must match the declared transaction ID array length.
        if (chunks.Count != expectedCount)
        {
            errors.Add(new ValidationError
            {
                Code = ChunkMissing,
                Message = $"Expected {expectedCount} chunk(s) but received {chunks.Count}.",
                Field = nameof(fileRef.ChunkTransactionIds)
            });
        }

        // 2. Schema-level max-chunks limit.
        if (schemaExtension is not null && chunks.Count > schemaExtension.MaxChunks)
        {
            errors.Add(new ValidationError
            {
                Code = TooManyChunks,
                Message = $"File has {chunks.Count} chunk(s) but the schema permits at most {schemaExtension.MaxChunks}.",
                Field = nameof(schemaExtension.MaxChunks)
            });
        }

        // 3. Platform max-chunks guard (independent of schema extension).
        if (chunks.Count > FileSchemaExtension.PlatformMaxChunks)
        {
            // Only add this error if we haven't already reported it via the schema extension path.
            if (schemaExtension is null || chunks.Count <= schemaExtension.MaxChunks)
            {
                errors.Add(new ValidationError
                {
                    Code = TooManyChunks,
                    Message = $"File has {chunks.Count} chunk(s) but the platform permits at most {FileSchemaExtension.PlatformMaxChunks}.",
                    Field = "chunks"
                });
            }
        }

        // Track seen indices for gap/duplicate detection.
        var seenIndices = new HashSet<int>();
        long totalSize = 0L;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];

            // 4. Null chunk — treat as missing.
            if (chunk is null)
            {
                errors.Add(new ValidationError
                {
                    Code = ChunkMissing,
                    Message = $"Chunk at position {i} is null or was not found.",
                    Field = $"chunks[{i}]"
                });
                continue;
            }

            // 5. File hash must match the file reference hash on every chunk.
            if (!string.Equals(chunk.FileHash, fileRef.Hash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError
                {
                    Code = ChunkHashMismatch,
                    Message = $"Chunk {i} has fileHash '{chunk.FileHash}' but the file reference declares '{fileRef.Hash}'.",
                    Field = $"chunks[{i}].{nameof(FileChunkMetadata.FileHash)}"
                });
            }

            // 6. Contiguous index check.
            if (!seenIndices.Add(chunk.ChunkIndex))
            {
                errors.Add(new ValidationError
                {
                    Code = ChunkIndexGap,
                    Message = $"Duplicate chunk index {chunk.ChunkIndex} detected at position {i}.",
                    Field = $"chunks[{i}].{nameof(FileChunkMetadata.ChunkIndex)}"
                });
            }
            else if (chunk.ChunkIndex != i)
            {
                errors.Add(new ValidationError
                {
                    Code = ChunkIndexGap,
                    Message = $"Chunk at position {i} has index {chunk.ChunkIndex}; expected {i}. Indices must be contiguous from 0.",
                    Field = $"chunks[{i}].{nameof(FileChunkMetadata.ChunkIndex)}"
                });
            }

            // 7. Per-chunk size limit (4 MB platform default).
            if (chunk.ChunkSize > FileSchemaExtension.DefaultChunkSizeBytes)
            {
                errors.Add(new ValidationError
                {
                    Code = ChunkOversized,
                    Message = $"Chunk {i} size {chunk.ChunkSize} bytes exceeds the maximum chunk size of {FileSchemaExtension.DefaultChunkSizeBytes} bytes (4 MB).",
                    Field = $"chunks[{i}].{nameof(FileChunkMetadata.ChunkSize)}"
                });
            }

            // 8. MIME-type allowlist check.
            if (schemaExtension is not null
                && schemaExtension.Accept.Length > 0
                && !schemaExtension.Accept.Contains(chunk.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError
                {
                    Code = MimeTypeMismatch,
                    Message = $"Chunk {i} has content type '{chunk.ContentType}' which is not in the permitted list: {string.Join(", ", schemaExtension.Accept)}.",
                    Field = $"chunks[{i}].{nameof(FileChunkMetadata.ContentType)}"
                });
            }

            totalSize += chunk.ChunkSize;
        }

        // 9. Total size — schema-level limit.
        if (schemaExtension is not null && !string.IsNullOrWhiteSpace(schemaExtension.MaxSizePerFile))
        {
            try
            {
                var schemaMaxBytes = FileSchemaExtension.ParseMaxSizeBytes(schemaExtension.MaxSizePerFile);
                if (totalSize > schemaMaxBytes)
                {
                    errors.Add(new ValidationError
                    {
                        Code = TotalSizeExceeded,
                        Message = $"Total file size {totalSize} bytes exceeds the schema limit of {schemaMaxBytes} bytes ({schemaExtension.MaxSizePerFile}).",
                        Field = nameof(schemaExtension.MaxSizePerFile)
                    });
                }
            }
            catch (ArgumentException)
            {
                // Unparseable MaxSizePerFile — fall through to platform limit only.
            }
        }

        // 10. Total size — platform hard limit (40 MB).
        if (totalSize > FileSchemaExtension.PlatformMaxSizeBytes)
        {
            errors.Add(new ValidationError
            {
                Code = TotalSizeExceeded,
                Message = $"Total file size {totalSize} bytes exceeds the platform maximum of {FileSchemaExtension.PlatformMaxSizeBytes} bytes (40 MB).",
                Field = "totalSize"
            });
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <summary>
    /// Validates the structural integrity of a single chunk's metadata record.
    /// </summary>
    /// <remarks>
    /// Checks performed:
    /// <list type="bullet">
    ///   <item><c>Type</c> must equal <see cref="FileChunkMetadata.MetadataType"/> ("file-chunk").</item>
    ///   <item><c>ChunkIndex</c> must equal <paramref name="expectedIndex"/>.</item>
    ///   <item><c>TotalChunks</c> must equal <paramref name="expectedTotalChunks"/>.</item>
    ///   <item><c>ChunkSize</c> must be positive and within the 4 MB per-chunk platform limit.</item>
    ///   <item><c>FileHash</c> must be non-empty.</item>
    /// </list>
    /// </remarks>
    /// <param name="chunk">The chunk metadata record to validate.</param>
    /// <param name="expectedIndex">The zero-based index this chunk should occupy.</param>
    /// <param name="expectedTotalChunks">The total number of chunks declared by the file reference.</param>
    /// <returns>
    /// <see cref="ValidationResult.Success()"/> when all checks pass;
    /// <see cref="ValidationResult.Failure(ValidationError[])"/> with every violation otherwise.
    /// </returns>
    public static ValidationResult ValidateChunkMetadata(
        FileChunkMetadata chunk,
        int expectedIndex,
        int expectedTotalChunks)
    {
        var errors = new List<ValidationError>();

        // Type discriminator.
        if (!string.Equals(chunk.Type, FileChunkMetadata.MetadataType, StringComparison.Ordinal))
        {
            errors.Add(new ValidationError
            {
                Code = InvalidChunkMetadata,
                Message = $"Chunk metadata type must be '{FileChunkMetadata.MetadataType}' but was '{chunk.Type}'.",
                Field = nameof(FileChunkMetadata.Type)
            });
        }

        // Index must match expected position.
        if (chunk.ChunkIndex != expectedIndex)
        {
            errors.Add(new ValidationError
            {
                Code = InvalidChunkMetadata,
                Message = $"Chunk index {chunk.ChunkIndex} does not match expected index {expectedIndex}.",
                Field = nameof(FileChunkMetadata.ChunkIndex)
            });
        }

        // Total chunk count declared inside the chunk must agree with the file reference.
        if (chunk.TotalChunks != expectedTotalChunks)
        {
            errors.Add(new ValidationError
            {
                Code = InvalidChunkMetadata,
                Message = $"Chunk declares {chunk.TotalChunks} total chunks but the file reference declares {expectedTotalChunks}.",
                Field = nameof(FileChunkMetadata.TotalChunks)
            });
        }

        // Chunk size must be positive.
        if (chunk.ChunkSize <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = InvalidChunkMetadata,
                Message = $"Chunk size must be greater than zero but was {chunk.ChunkSize}.",
                Field = nameof(FileChunkMetadata.ChunkSize)
            });
        }
        else if (chunk.ChunkSize > FileSchemaExtension.DefaultChunkSizeBytes)
        {
            errors.Add(new ValidationError
            {
                Code = ChunkOversized,
                Message = $"Chunk size {chunk.ChunkSize} bytes exceeds the maximum of {FileSchemaExtension.DefaultChunkSizeBytes} bytes (4 MB).",
                Field = nameof(FileChunkMetadata.ChunkSize)
            });
        }

        // File hash must be present.
        if (string.IsNullOrWhiteSpace(chunk.FileHash))
        {
            errors.Add(new ValidationError
            {
                Code = InvalidChunkMetadata,
                Message = "Chunk metadata must include a non-empty fileHash.",
                Field = nameof(FileChunkMetadata.FileHash)
            });
        }

        return errors.Count > 0
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <summary>
    /// Validates an optional file field value.
    /// Returns <see cref="ValidationResult.Success()"/> when no file has been provided
    /// (i.e. the value is <see langword="null"/> or a JSON null/undefined element).
    /// Returns <see cref="ValidationResult.Failure(string, string, string?)"/> when a non-null
    /// value is present where none was expected.
    /// </summary>
    /// <param name="fieldValue">
    /// The raw field value from the action payload. May be <see langword="null"/>,
    /// a <see cref="JsonElement"/>, or any other object.
    /// </param>
    /// <returns>
    /// <see cref="ValidationResult.Success()"/> when the field is empty or absent;
    /// a failure result otherwise.
    /// </returns>
    public static ValidationResult ValidateOptionalFileField(object? fieldValue)
    {
        if (fieldValue is null)
            return ValidationResult.Success();

        if (fieldValue is JsonElement element
            && (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return ValidationResult.Success();
        }

        return ValidationResult.Failure(
            InvalidChunkMetadata,
            "Expected null for optional empty file field but a value was present.",
            "fileField");
    }
}
