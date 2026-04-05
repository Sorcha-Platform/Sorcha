// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

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
                "On the first chunk (no uploadSessionId), the server creates an upload session, " +
                "stores the master key server-side, and returns an opaque uploadSessionId plus the " +
                "salt. Subsequent chunks must supply the uploadSessionId to reuse the same encryption " +
                "session. The salt (not the master key) is returned so the client can build a FileReference.")
            .Produces<FileChunkSubmissionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status413RequestEntityTooLarge);
    }

    private static async Task<IResult> SubmitFileChunk(
        [FromBody] FileChunkSubmissionRequest request,
        IActionStore actionStore,
        ITransactionBuilderService txBuilder,
        IMemoryCache memoryCache,
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

        // Resolve or create the server-side upload session (master key + salt).
        // The master key never leaves the server — only the opaque session ID and the
        // non-secret salt are returned to the client.
        byte[] masterFileKey;
        byte[] salt;
        string uploadSessionId;

        if (!string.IsNullOrEmpty(request.UploadSessionId))
        {
            // Subsequent chunk: look up the existing session from cache
            var cacheKey = $"file-upload-session:{request.UploadSessionId}";
            if (!memoryCache.TryGetValue(cacheKey, out FileUploadSessionCache? sessionCache) || sessionCache is null)
            {
                return Results.BadRequest(new
                {
                    error = "Upload session not found or expired. Upload sessions last 30 minutes. " +
                            "Please restart the upload from the first chunk."
                });
            }

            masterFileKey = sessionCache.MasterFileKey;
            salt = sessionCache.Salt;
            uploadSessionId = request.UploadSessionId;
        }
        else
        {
            // First chunk: create a new session and cache it with a 30-minute TTL
            var session = txBuilder.CreateFileUploadSession();
            masterFileKey = session.MasterFileKey;
            salt = session.Salt;
            uploadSessionId = Guid.NewGuid().ToString("N");

            var cacheKey = $"file-upload-session:{uploadSessionId}";
            memoryCache.Set(cacheKey, new FileUploadSessionCache(masterFileKey, salt),
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30),
                    Size = 1
                });
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
                UploadSessionId = uploadSessionId,
                SaltBase64 = Convert.ToBase64String(salt)
            });
    }
}

/// <summary>
/// Server-side state for an in-progress multi-chunk file upload session.
/// Stored in <see cref="IMemoryCache"/> for the lifetime of the upload (30 minutes).
/// The master key is held exclusively on the server; only the opaque session ID and
/// the non-secret salt are returned to the client.
/// </summary>
internal sealed record FileUploadSessionCache(byte[] MasterFileKey, byte[] Salt);

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
    /// Opaque upload session identifier returned by the server on the first chunk response.
    /// Omit on the first chunk — the server creates a new session and returns the ID.
    /// Required on chunks 1+ so the server can retrieve the shared encryption session.
    /// </summary>
    public string? UploadSessionId { get; init; }
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
    /// Opaque upload session identifier. Must be included in all subsequent chunk requests
    /// for the same file so the server can retrieve the shared encryption session.
    /// The master key is held exclusively on the server and is never returned to the client.
    /// </summary>
    public required string UploadSessionId { get; init; }

    /// <summary>
    /// Base64-encoded 32-byte salt for this upload session.
    /// Not secret — store this in the FileReference so the validator can derive per-chunk keys.
    /// </summary>
    public required string SaltBase64 { get; init; }
}
