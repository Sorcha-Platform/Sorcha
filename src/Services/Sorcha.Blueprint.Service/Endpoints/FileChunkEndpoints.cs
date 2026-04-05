// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceDefaults;
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
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Strict);

        group.MapPost("/", SubmitFileChunk)
            .WithName("SubmitFileChunk")
            .WithSummary("Submit an encrypted file chunk")
            .WithDescription(
                "Accepts a single Base64-encoded file chunk, encrypts it server-side using an " +
                "HKDF-derived XChaCha20-Poly1305 key, and stages it for inclusion in a blueprint " +
                "action transaction. Chunks are submitted individually and assembled by the validator " +
                "once all chunks for a file have been received. The caller must submit all chunks " +
                "(0 to totalChunks-1) before the parent action can be finalised. " +
                "Each chunk must not exceed 4 MB and the total chunk count must not exceed 10. " +
                "On the first chunk (chunkIndex 0), the server generates a master key and salt " +
                "and returns them in the response. Subsequent chunks must supply the same values.")
            .Produces<FileChunkSubmissionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413RequestEntityTooLarge);
    }

    private static async Task<IResult> SubmitFileChunk(
        [FromBody] FileChunkSubmissionRequest request,
        IActionStore actionStore,
        ITransactionBuilderService txBuilder,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.FileChunks");

        // Require authenticated JWT subject for audit trail
        var jwtSubject = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? httpContext.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(jwtSubject))
            return Results.Unauthorized();

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

        // Validate SenderWallet
        if (string.IsNullOrWhiteSpace(request.SenderWallet))
            return Results.BadRequest(new { error = "SenderWallet is required." });

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
                "Chunk {ChunkIndex}/{TotalChunks} for file {FileHash} from JWT {Subject} (wallet {Wallet}) exceeds size limit: {ActualBytes} bytes",
                request.ChunkIndex, request.TotalChunks, request.FileHash, jwtSubject, request.SenderWallet, decodedBytes.Length);

            return Results.StatusCode(StatusCodes.Status413RequestEntityTooLarge);
        }

        // Resolve or generate the upload session (master key + salt).
        // First chunk (or absent session fields): server generates a fresh session.
        // Subsequent chunks: caller supplies the master key and salt from the first chunk response.
        byte[] masterFileKey;
        byte[] salt;

        if (request.MasterKeyBase64 is not null && request.SaltBase64 is not null)
        {
            try
            {
                masterFileKey = Convert.FromBase64String(request.MasterKeyBase64);
                salt = Convert.FromBase64String(request.SaltBase64);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new { error = "MasterKeyBase64 or SaltBase64 is not valid Base64." });
            }
        }
        else
        {
            var session = txBuilder.CreateFileUploadSession();
            masterFileKey = session.MasterFileKey;
            salt = session.Salt;
        }

        // Encrypt the chunk server-side using a per-chunk HKDF-derived key
        var encrypted = await txBuilder.EncryptFileChunkAsync(
            decodedBytes, masterFileKey, salt, request.ChunkIndex, request.SenderWallet, cancellationToken);

        // Generate chunk transaction ID using CSPRNG to avoid hash-based collisions
        var chunkTxId = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow;

        // Build chunk metadata for validator access
        var chunkMetadata = new FileChunkMetadata
        {
            Type = FileChunkMetadata.MetadataType,
            ChunkIndex = request.ChunkIndex,
            TotalChunks = request.TotalChunks,
            FileHash = request.FileHash,
            ContentType = request.ContentType,
            ChunkSize = encrypted.EncryptedContent.Length
        };

        // Store encrypted chunk content and metadata
        await actionStore.StoreFileContentAsync(chunkTxId, encrypted.EncryptedContent);
        await actionStore.StoreFileMetadataAsync("pending", chunkTxId, new FileMetadata
        {
            FileId = chunkTxId,
            FileName = $"{request.FileHash}-chunk{request.ChunkIndex}",
            ContentType = request.ContentType,
            Size = encrypted.EncryptedContent.Length,
            CustomMetadata = chunkMetadata.ToJson()
        });

        logger.LogInformation(
            "Stored encrypted file chunk {ChunkIndex}/{TotalChunks} for file {FileHash} as transaction {ChunkTxId} ({Bytes} bytes encrypted) — JWT {Subject} wallet {Wallet}",
            request.ChunkIndex, request.TotalChunks, request.FileHash, chunkTxId, encrypted.EncryptedContent.Length, jwtSubject, request.SenderWallet);

        return Results.Created(
            $"/api/file-chunks/{chunkTxId}",
            new FileChunkSubmissionResponse
            {
                ChunkTransactionId = chunkTxId,
                ChunkIndex = request.ChunkIndex,
                Timestamp = timestamp,
                MasterKeyBase64 = Convert.ToBase64String(masterFileKey),
                SaltBase64 = Convert.ToBase64String(salt)
            });
    }
}

/// <summary>
/// Request to submit a single file chunk for server-side encryption and staging.
/// </summary>
public record FileChunkSubmissionRequest
{
    /// <summary>
    /// Wallet address of the submitting participant. Required for audit logging.
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
    /// SHA-256 hash of the complete original file (before chunking), prefixed with "sha256:".
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// MIME type of the original file (e.g. "application/pdf", "image/png").
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Base64-encoded raw chunk bytes. Must decode to at most 4 MB.
    /// The server encrypts these bytes before storage.
    /// </summary>
    public required string ContentBase64 { get; init; }

    /// <summary>
    /// Base64-encoded 32-byte master file key from a previous chunk response.
    /// Omit on the first chunk — the server generates a new session.
    /// Required on chunks 1+ to maintain a consistent encryption session across all chunks.
    /// </summary>
    public string? MasterKeyBase64 { get; init; }

    /// <summary>
    /// Base64-encoded 32-byte salt from a previous chunk response.
    /// Omit on the first chunk — the server generates a new session.
    /// Required on chunks 1+ to maintain a consistent encryption session across all chunks.
    /// </summary>
    public string? SaltBase64 { get; init; }
}

/// <summary>
/// Response returned after a file chunk has been successfully encrypted and staged.
/// </summary>
public record FileChunkSubmissionResponse
{
    /// <summary>
    /// Unique transaction ID assigned to this chunk (CSPRNG UUID, 32 hex chars).
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

    /// <summary>
    /// Base64-encoded 32-byte master file key for this upload session.
    /// Must be supplied unchanged in all subsequent chunk requests for the same file.
    /// </summary>
    public required string MasterKeyBase64 { get; init; }

    /// <summary>
    /// Base64-encoded 32-byte salt for this upload session.
    /// Must be supplied unchanged in all subsequent chunk requests for the same file.
    /// </summary>
    public required string SaltBase64 { get; init; }
}
