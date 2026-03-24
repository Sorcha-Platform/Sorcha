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

        // Check if file metadata already exists
        var existing = await context.FileMetadata
            .FirstOrDefaultAsync(f => f.Id == fileId && f.TransactionHash == transactionHash);

        if (existing is not null)
        {
            _logger.LogDebug("File metadata {FileId} already exists for transaction {TransactionHash}",
                fileId, transactionHash);
            return;
        }

        var entity = new FileMetadataEntity
        {
            Id = fileId,
            TransactionHash = transactionHash,
            FileName = metadata.FileName,
            ContentType = metadata.ContentType,
            Size = metadata.Size,
            Content = [] // Content stored separately via StoreFileContentAsync
        };

        context.FileMetadata.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation("Stored file metadata {FileId} for transaction {TransactionHash}",
            fileId, transactionHash);
    }

    /// <inheritdoc/>
    public async Task<FileMetadata?> GetFileMetadataAsync(string transactionHash, string fileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.FileMetadata.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.TransactionHash == transactionHash);

        if (entity is null)
        {
            return null;
        }

        return new FileMetadata
        {
            FileId = entity.Id,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            Size = entity.Size
        };
    }

    /// <inheritdoc/>
    public async Task StoreFileContentAsync(string fileId, byte[] content)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.FileMetadata.FirstOrDefaultAsync(f => f.Id == fileId);
        if (entity is null)
        {
            _logger.LogWarning("Cannot store file content: file metadata {FileId} not found", fileId);
            return;
        }

        entity.Content = content;
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
