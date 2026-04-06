// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using Sorcha.Blueprint.Service.Models.Responses;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IActionStore"/>.
/// Registered as a singleton; uses <see cref="IDbContextFactory{TContext}"/>
/// to create scoped <see cref="BlueprintDbContext"/> instances per operation.
/// </summary>
public class EfCoreActionStore : IActionStore
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCoreActionStore> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreActionStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    public EfCoreActionStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreActionStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ActionDetailsResponse> StoreActionAsync(ActionDetailsResponse action)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = new ActionEntity
        {
            TransactionHash = action.TransactionHash,
            WalletAddress = action.SenderWallet,
            RegisterAddress = action.RegisterAddress,
            Content = JsonSerializer.Serialize(action, SerializerOptions),
            CreatedAt = action.Timestamp != default ? action.Timestamp : DateTimeOffset.UtcNow
        };

        context.Actions.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation("Stored action {TransactionHash} for wallet {Wallet}",
            action.TransactionHash, action.SenderWallet);

        return action;
    }

    /// <inheritdoc/>
    public async Task<ActionDetailsResponse?> GetActionAsync(string transactionHash)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.Actions.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TransactionHash == transactionHash);

        if (entity is null)
        {
            return null;
        }

        return DeserializeAction(entity);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ActionDetailsResponse>> GetActionsAsync(
        string walletAddress,
        string registerAddress,
        int skip = 0,
        int take = 20)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.Actions.AsNoTracking()
            .Where(a => a.WalletAddress == walletAddress && a.RegisterAddress == registerAddress)
            .OrderByDescending(a => a.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return entities
            .Select(DeserializeAction)
            .Where(a => a is not null)
            .Cast<ActionDetailsResponse>();
    }

    /// <inheritdoc/>
    public async Task<int> GetActionCountAsync(string walletAddress, string registerAddress)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return await context.Actions.CountAsync(
            a => a.WalletAddress == walletAddress && a.RegisterAddress == registerAddress);
    }

    /// <inheritdoc/>
    public async Task StoreFileMetadataAsync(string transactionHash, string fileId, FileMetadata metadata)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // "pending" is the sentinel value used by FileChunkEndpoints when a chunk has been staged
        // but not yet claimed by a finalised action.  Store null for the FK so that the database
        // FK constraint (TransactionHash → Actions.TransactionHash) is not violated.
        string? resolvedHash = transactionHash == "pending" ? null : transactionHash;

        // Check if file metadata already exists (keyed by fileId only — transactionHash may be null)
        var existing = await context.FileMetadata
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (existing is not null)
        {
            // If the row already exists with a real hash, do not overwrite it
            if (existing.TransactionHash is not null)
            {
                _logger.LogDebug("File metadata {FileId} already claimed by transaction {TransactionHash}",
                    fileId, existing.TransactionHash);
                return;
            }

            // Update the pending row with the finalised transaction hash if provided
            if (resolvedHash is not null)
            {
                existing.TransactionHash = resolvedHash;
                await context.SaveChangesAsync();
                _logger.LogInformation("Claimed file metadata {FileId} for transaction {TransactionHash}",
                    fileId, resolvedHash);
            }
            else
            {
                _logger.LogDebug("File metadata {FileId} already staged as pending", fileId);
            }

            return;
        }

        var entity = new FileMetadataEntity
        {
            Id = fileId,
            TransactionHash = resolvedHash,
            FileName = metadata.FileName,
            ContentType = metadata.ContentType,
            Size = metadata.Size,
            Content = [], // Content stored separately via StoreFileContentAsync
            CreatedAt = DateTimeOffset.UtcNow,
            CustomMetadata = metadata.CustomMetadata
        };

        context.FileMetadata.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation("Stored file metadata {FileId} for transaction {TransactionHash}",
            fileId, resolvedHash ?? "pending");
    }

    /// <inheritdoc/>
    public async Task<FileMetadata?> GetFileMetadataAsync(string transactionHash, string fileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Look up by fileId only — the TransactionHash may be null for staged (pending) chunks.
        // If a specific non-pending hash is requested, also verify the hash matches.
        var entity = await context.FileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId &&
                (f.TransactionHash == transactionHash || f.TransactionHash == null));

        if (entity is null)
        {
            return null;
        }

        return new FileMetadata
        {
            FileId = entity.Id,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size,
            CustomMetadata = entity.CustomMetadata
        };
    }

    /// <inheritdoc/>
    public async Task StoreFileContentAsync(string fileId, byte[] content)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.FileMetadata.FirstOrDefaultAsync(f => f.Id == fileId);
        if (entity is null)
        {
            // Chunk content arrives before metadata in the FileChunkEndpoints flow.
            // Create a minimal placeholder row (TransactionHash = null = "pending") so
            // the content is persisted.  StoreFileMetadataAsync will fill in the rest.
            entity = new FileMetadataEntity
            {
                Id = fileId,
                TransactionHash = null,
                FileName = string.Empty,
                ContentType = string.Empty,
                Size = content.Length,
                Content = content,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.FileMetadata.Add(entity);
        }
        else
        {
            entity.Content = content;
        }

        await context.SaveChangesAsync();

        _logger.LogInformation("Stored file content for {FileId} ({Size} bytes)", fileId, content.Length);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetFileContentAsync(string fileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.FileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (entity is null || entity.Content.Length == 0)
        {
            return null;
        }

        return entity.Content;
    }

    /// <inheritdoc/>
    public async Task<string?> GetByIdempotencyKeyAsync(string idempotencyKey)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.Actions.AsNoTracking()
            .FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey);

        if (entity is null)
        {
            return null;
        }

        // Check expiry
        if (entity.IdempotencyExpiry.HasValue && entity.IdempotencyExpiry.Value < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Idempotency key {Key} has expired", idempotencyKey);
            return null;
        }

        return entity.TransactionHash;
    }

    /// <inheritdoc/>
    public async Task StoreIdempotencyKeyAsync(string idempotencyKey, string transactionHash, TimeSpan ttl)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.Actions.FirstOrDefaultAsync(a => a.TransactionHash == transactionHash);
        if (entity is null)
        {
            _logger.LogWarning(
                "Cannot store idempotency key: action {TransactionHash} not found", transactionHash);
            return;
        }

        entity.IdempotencyKey = idempotencyKey;
        entity.IdempotencyExpiry = DateTimeOffset.UtcNow.Add(ttl);
        await context.SaveChangesAsync();

        _logger.LogInformation("Stored idempotency key for transaction {TransactionHash} (TTL: {TTL})",
            transactionHash, ttl);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<(string FileId, string TransactionHash)>> GetOrphanedFileMetadataAsync(
        DateTimeOffset olderThan)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Chunks are stored with TransactionHash = null until an action claims them ("pending" state).
        // A chunk is an orphan when it is still unclaimed (null FK) AND old enough that
        // no in-flight upload could still be completing.
        // Claimed chunks (TransactionHash IS NOT NULL) are never orphans — they belong to
        // a finalised action regardless of whether the action row exists yet.
        var orphans = await context.FileMetadata
            .AsNoTracking()
            .Where(f => f.TransactionHash == null && f.CreatedAt < olderThan)
            .Select(f => new { f.Id })
            .ToListAsync();

        return orphans
            .Select(o => (o.Id, (string)"pending"))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task DeleteFileMetadataAsync(string fileId, string transactionHash)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        // Orphan rows have TransactionHash = null (the "pending" sentinel).
        // Look up by fileId only — the caller passes "pending" as a logical sentinel
        // but the DB stores null for unclaimed chunks.
        var entity = await context.FileMetadata
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (entity is null)
        {
            _logger.LogDebug(
                "DeleteFileMetadataAsync: file {FileId} not found — already removed",
                fileId);
            return;
        }

        // Only delete truly unclaimed (null hash) rows to avoid removing claimed chunks
        if (entity.TransactionHash is not null)
        {
            _logger.LogWarning(
                "DeleteFileMetadataAsync: file {FileId} is claimed by transaction {TransactionHash} — skipping delete",
                fileId, entity.TransactionHash);
            return;
        }

        context.FileMetadata.Remove(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation(
            "Deleted orphan file metadata {FileId} (unclaimed pending chunk)",
            fileId);
    }

    private ActionDetailsResponse? DeserializeAction(ActionEntity entity)
    {
        try
        {
            return JsonSerializer.Deserialize<ActionDetailsResponse>(entity.Content, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize action {TransactionHash} from entity content",
                entity.TransactionHash);
            return null;
        }
    }
}
