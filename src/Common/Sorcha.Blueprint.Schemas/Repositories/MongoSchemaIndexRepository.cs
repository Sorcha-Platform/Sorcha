// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Repositories;

/// <summary>
/// MongoDB implementation of schema index repository.
/// Stores lightweight metadata entries in the schemaIndex collection.
/// </summary>
public class MongoSchemaIndexRepository : ISchemaIndexRepository
{
    private readonly IMongoCollection<SchemaIndexEntryDocument> _collection;
    private readonly ILogger<MongoSchemaIndexRepository> _logger;
    private readonly SemaphoreSlim _indexCreationLock = new(1, 1);
    private bool _indexesCreated;

    public MongoSchemaIndexRepository(
        IMongoDatabase database,
        ILogger<MongoSchemaIndexRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _collection = database.GetCollection<SchemaIndexEntryDocument>("schemaIndex");
    }

    // Idempotent + concurrency-safe + non-throwing. Mirrors ValidatorRegistry's
    // EnsureMongoIndexesAsync pattern. Must be invoked from every public method
    // — read paths AND write paths — because the per-instance flag means a
    // process that only ever upserts would otherwise never trigger index
    // creation, leaving the collection on _id_ only (the bug fixed here).
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        if (_indexesCreated) return;

        await _indexCreationLock.WaitAsync(cancellationToken);
        try
        {
            if (_indexesCreated) return;
            await CreateIndexesAsync();
            _indexesCreated = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create schemaIndex MongoDB indexes — will retry on next operation");
        }
        finally
        {
            _indexCreationLock.Release();
        }
    }

    private async Task CreateIndexesAsync()
    {
        _logger.LogInformation("Creating MongoDB indexes for schema index collection");

        var indexes = new List<CreateIndexModel<SchemaIndexEntryDocument>>
        {
            // 1. Unique composite: sourceProvider + sourceUri
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Ascending(d => d.SourceProvider)
                .Ascending(d => d.SourceUri),
                new CreateIndexOptions { Unique = true, Name = "idx_provider_uri_unique" }),

            // 2. Text index: title, description, keywords
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Text(d => d.Title)
                .Text(d => d.Description)
                .Text(d => d.Keywords),
                new CreateIndexOptions { Name = "idx_text_search" }),

            // 3. Compound: sectorTags + status
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Ascending(d => d.SectorTags)
                .Ascending(d => d.Status),
                new CreateIndexOptions { Name = "idx_sector_status" }),

            // 4. Single: sourceProvider
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Ascending(d => d.SourceProvider),
                new CreateIndexOptions { Name = "idx_provider" }),

            // 5. Single: lastFetchedAt
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Ascending(d => d.LastFetchedAt),
                new CreateIndexOptions { Name = "idx_last_fetched" }),

            // 6. Unique: shortCode for URL-safe lookups
            new(Builders<SchemaIndexEntryDocument>.IndexKeys
                .Ascending(d => d.ShortCode),
                new CreateIndexOptions { Unique = true, Name = "idx_shortcode_unique" })
        };

        await _collection.Indexes.CreateManyAsync(indexes);
        _logger.LogInformation("Schema index MongoDB indexes created successfully");
    }

    /// <inheritdoc />
    public async Task<SchemaIndexSearchResult> SearchAsync(
        string? search = null,
        string[]? sectors = null,
        string? provider = null,
        SchemaIndexStatus? status = null,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filterBuilder = Builders<SchemaIndexEntryDocument>.Filter;
        var filters = new List<FilterDefinition<SchemaIndexEntryDocument>>();

        // Full-text search
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchRegex = new BsonRegularExpression(search, "i");
            filters.Add(filterBuilder.Or(
                filterBuilder.Regex(d => d.Title, searchRegex),
                filterBuilder.Regex(d => d.Description, searchRegex)));
        }

        // Sector filter
        if (sectors is { Length: > 0 })
        {
            filters.Add(filterBuilder.AnyIn(d => d.SectorTags, sectors));
        }

        // Provider filter
        if (!string.IsNullOrWhiteSpace(provider))
        {
            filters.Add(filterBuilder.Eq(d => d.SourceProvider, provider));
        }

        // Status filter (default to Active)
        var statusStr = (status ?? SchemaIndexStatus.Active).ToString();
        filters.Add(filterBuilder.Eq(d => d.Status, statusStr));

        // Cursor pagination
        if (!string.IsNullOrEmpty(cursor))
        {
            filters.Add(filterBuilder.Gt(d => d.Id, cursor));
        }

        var combinedFilter = filters.Count > 0
            ? filterBuilder.And(filters)
            : filterBuilder.Empty;

        var totalCount = (int)await _collection.CountDocumentsAsync(combinedFilter, cancellationToken: cancellationToken);

        var docs = await _collection.Find(combinedFilter)
            .SortBy(d => d.Title)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        var nextCursor = docs.Count == limit ? docs[^1].Id : null;

        return new SchemaIndexSearchResult(docs, totalCount, nextCursor);
    }

    /// <inheritdoc />
    public async Task<SchemaIndexEntryDocument?> GetByProviderAndUriAsync(
        string sourceProvider,
        string sourceUri,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filter = Builders<SchemaIndexEntryDocument>.Filter.And(
            Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceProvider, sourceProvider),
            Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceUri, sourceUri));

        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SchemaIndexEntryDocument?> GetByShortCodeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filter = Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.ShortCode, shortCode);
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        SchemaIndexEntryDocument entry,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filter = Builders<SchemaIndexEntryDocument>.Filter.And(
            Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceProvider, entry.SourceProvider),
            Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceUri, entry.SourceUri));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

        // Skip update if content hash unchanged
        if (existing is not null && existing.ContentHash == entry.ContentHash)
        {
            return false;
        }

        if (existing is not null)
        {
            entry.Id = existing.Id;
            entry.ShortCode = existing.ShortCode;
            entry.DateAdded = existing.DateAdded;
        }
        else
        {
            entry.Id = ObjectId.GenerateNewId().ToString();
            entry.ShortCode = GenerateShortCode();
        }

        entry.DateModified = DateTimeOffset.UtcNow;

        var options = new ReplaceOptions { IsUpsert = true };
        var result = await _collection.ReplaceOneAsync(filter, entry, options, cancellationToken);

        return result.ModifiedCount > 0 || result.UpsertedId is not null;
    }

    /// <inheritdoc />
    public async Task<int> BatchUpsertAsync(
        IEnumerable<SchemaIndexEntryDocument> entries,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (await UpsertAsync(entry, cancellationToken))
            {
                count++;
            }
        }
        return count;
    }

    /// <inheritdoc />
    public async Task UpdateProviderStatusAsync(
        string sourceProvider,
        SchemaIndexStatus status,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filter = Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceProvider, sourceProvider);
        var update = Builders<SchemaIndexEntryDocument>.Update
            .Set(d => d.Status, status.ToString())
            .Set(d => d.DateModified, DateTimeOffset.UtcNow);

        await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetCountByProviderAsync(
        string sourceProvider,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync(cancellationToken);
        var filter = Builders<SchemaIndexEntryDocument>.Filter.Eq(d => d.SourceProvider, sourceProvider);
        return (int)await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return (int)await _collection.CountDocumentsAsync(
            Builders<SchemaIndexEntryDocument>.Filter.Empty,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Generates a 6-character alphanumeric short code for URL-safe lookups.
    /// </summary>
    private static string GenerateShortCode()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(6, bytes.ToArray(), (span, b) =>
        {
            for (int i = 0; i < span.Length; i++)
                span[i] = chars[b[i] % chars.Length];
        });
    }
}
