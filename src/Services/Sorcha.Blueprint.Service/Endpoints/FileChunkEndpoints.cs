// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.TransactionHandler.Chunking;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// REST endpoints for staged file chunk submission in the Blueprint Service.
/// </summary>
public static class FileChunkEndpoints
{
    /// <summary>
    /// Maps file chunk submission endpoints.
    /// </summary>
    public static void MapFileChunkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/file-chunks")
            .WithTags("FileChunks")
            .RequireAuthorization();

        group.MapPost("/", SubmitFileChunk)
            .WithName("SubmitFileChunk")
            .WithSummary("Submit an encrypted file chunk")
            .WithDescription(
                "Accepts a single Base64-encoded encrypted file chunk and stages it for inclusion in a " +
                "blueprint action transaction. Chunks are submitted individually and assembled by the " +
                "validator once all chunks for a file have been received. The caller must submit all " +
                "chunks (0 to totalChunks-1) before the parent action can be finalised. " +
                "Each chunk must not exceed 4 MB and the total chunk count must not exceed 10.")
            .Produces<FileChunkSubmissionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status413RequestEntityTooLarge);
    }

    private static async Task<IResult> SubmitFileChunk(
        [FromBody] FileChunkSubmissionRequest request,
        IActionStore actionStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.FileChunkEndpoints");

        // Validate TotalChunks
        if (request.TotalChunks < 1 || request.TotalChunks > 10)
            return Results.BadRequest(new { error = "TotalChunks must be between 1 and 10." });

        // Validate ChunkIndex
        if (request.ChunkIndex < 0 || request.ChunkIndex >= request.TotalChunks)
            return Results.BadRequest(new { error = $"ChunkIndex must be between 0 and {request.TotalChunks - 1} (inclusive)." });

        // Validate FileHash
        if (string.IsNullOrWhiteSpace(request.FileHash))
            return Results.BadRequest(new { error = "FileHash is required." });

        // Validate ContentType
        if (string.IsNullOrWhiteSpace(request.ContentType))
            return Results.BadRequest(new { error = "ContentType is required." });

        // Decode and validate content
        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { error = "ContentBase64 is not valid Base64." });
        }

        // Validate chunk size
        if (decodedBytes.Length > FileSchemaExtension.DefaultChunkSizeBytes)
        {
            logger.LogWarning(
                "Chunk {ChunkIndex}/{TotalChunks} for file {FileHash} exceeds size limit: {ActualBytes} bytes",
                request.ChunkIndex, request.TotalChunks, request.FileHash, decodedBytes.Length);

            return Results.StatusCode(StatusCodes.Status413RequestEntityTooLarge);
        }

        // Generate chunk transaction ID: SHA-256(fileHash + chunkIndex + timestamp)
        var timestamp = DateTimeOffset.UtcNow;
        var hashInput = $"{request.FileHash}{request.ChunkIndex}{timestamp:O}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var chunkTxId = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Build chunk metadata
        var fileMetadata = new FileChunkMetadata
        {
            Type = FileChunkMetadata.MetadataType,
            ChunkIndex = request.ChunkIndex,
            TotalChunks = request.TotalChunks,
            FileHash = request.FileHash,
            ContentType = request.ContentType,
            ChunkSize = decodedBytes.Length
        };

        // Store chunk content and metadata
        await actionStore.StoreFileContentAsync(chunkTxId, decodedBytes);
        await actionStore.StoreFileMetadataAsync("pending", chunkTxId, new FileMetadata
        {
            FileId = chunkTxId,
            FileName = $"{request.FileHash}-chunk{request.ChunkIndex}",
            ContentType = request.ContentType,
            Size = decodedBytes.Length
        });

        logger.LogInformation(
            "Stored file chunk {ChunkIndex}/{TotalChunks} for file {FileHash} as transaction {ChunkTxId} ({Bytes} bytes)",
            request.ChunkIndex, request.TotalChunks, request.FileHash, chunkTxId, decodedBytes.Length);

        return Results.Created(
            $"/api/file-chunks/{chunkTxId}",
            new FileChunkSubmissionResponse
            {
                ChunkTransactionId = chunkTxId,
                ChunkIndex = request.ChunkIndex,
                Timestamp = timestamp
            });
    }
}

/// <summary>
/// Request to submit a single encrypted file chunk.
/// </summary>
public record FileChunkSubmissionRequest
{
    /// <summary>
    /// Wallet address of the submitting participant.
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// Address of the register this file chunk is associated with.
    /// </summary>
    public required string RegisterAddress { get; init; }

    /// <summary>
    /// Zero-based index of this chunk within the ordered sequence.
    /// </summary>
    public required int ChunkIndex { get; init; }

    /// <summary>
    /// Total number of chunks that make up the complete file transfer.
    /// Must be between 1 and 10.
    /// </summary>
    public required int TotalChunks { get; init; }

    /// <summary>
    /// SHA-256 hash of the complete original file (before chunking).
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// MIME type of the original file (e.g. "application/pdf", "image/png").
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Base64-encoded raw chunk bytes (encrypted payload). Must decode to at most 4 MB.
    /// </summary>
    public required string ContentBase64 { get; init; }
}

/// <summary>
/// Response returned after a file chunk has been successfully staged.
/// </summary>
public record FileChunkSubmissionResponse
{
    /// <summary>
    /// Unique transaction ID assigned to this chunk, derived from SHA-256(fileHash + chunkIndex + timestamp).
    /// </summary>
    public required string ChunkTransactionId { get; init; }

    /// <summary>
    /// Zero-based index of the stored chunk, echoed from the request.
    /// </summary>
    public required int ChunkIndex { get; init; }

    /// <summary>
    /// UTC timestamp at which the chunk was accepted and staged.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}
