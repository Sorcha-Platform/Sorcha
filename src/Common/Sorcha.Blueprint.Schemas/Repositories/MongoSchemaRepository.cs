// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Repositories;

/// <summary>
/// MongoDB implementation of ISchemaRepository.
/// Provides persistent storage for custom and external schemas.
/// </summary>
public class MongoSchemaRepository : ISchemaRepository
{
    private readonly IMongoCollection<MongoSchemaDocument> _collection;
    private readonly ILogger<MongoSchemaRepository> _logger;
    private readonly bool _createIndexesOnStartup;
    private bool _indexesCreated;

    /// <summary>
    /// Initializes a new instance with configuration options.
    /// </summary>
    public MongoSchemaRepository(
        IOptions<MongoSchemaStorageConfiguration> options,
        ILogger<MongoSchemaRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options?.Value);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = options.Value;
        var client = new MongoClient(config.ConnectionString);
        var database = client.GetDatabase(config.DatabaseName);
        _collection = database.GetCollection<MongoSchemaDocument>(config.CollectionName);

        _createIndexesOnStartup = config.CreateIndexesOnStartup;
    }

    /// <summary>
    /// Initializes a new instance for testing with explicit database.
    /// </summary>
    public MongoSchemaRepository(
        IMongoDatabase database,
        string collectionName,
        ILogger<MongoSchemaRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrEmpty(collectionName);

        _collection = database.GetCollection<MongoSchemaDocument>(collectionName);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Ensures indexes are created on first access when configured.
    /// Uses lazy initialization to avoid synchronous-over-async deadlocks in the constructor.
    /// </summary>
    private async Task EnsureIndexesAsync()
    {
        if (_createIndexesOnStartup && !_indexesCreated)
        {
            await CreateIndexesAsync();
            _indexesCreated = true;
        }
    }

    /// <summary>
    /// Creates indexes for optimal query performance.
    /// </summary>
    private async Task CreateIndexesAsync()
    {
        _logger.LogInformation("Creating MongoDB indexes for schema storage");

        var indexes = new List<CreateIndexModel<MongoSchemaDocument>>
        {
            // Unique index on identifier + organization (allows same identifier in different orgs)
            new(Builders<MongoSchemaDocument>.IndexKeys
                .Ascending(s => s.Identifier)
                .Ascending(s => s.OrganizationId),
                new CreateIndexOptions { Unique = true }),

            // Index for organization queries
            new(Builders<MongoSchemaDocument>.IndexKeys.Ascending(s => s.OrganizationId)),

            // Index for category filtering
            new(Builders<MongoSchemaDocument>.IndexKeys.Ascending(s => s.Category)),

            // Index for status filtering
            new(Builders<MongoSchemaDocument>.IndexKeys.Ascending(s => s.Status)),

            // Index for globally published schemas
            new(Builders<MongoSchemaDocument>.IndexKeys.Ascending(s => s.IsGloballyPublished)),

            // Text index for search on title and description
            new(Builders<MongoSchemaDocument>.IndexKeys
                .Text(s => s.Title)
                .Text(s => s.Description)),

            // Composite index for common list queries
            new(Builders<MongoSchemaDocument>.IndexKeys
                .Ascending(s => s.Category)
                .Ascending(s => s.Status)
                .Ascending(s => s.OrganizationId))
        };

        await _collection.Indexes.CreateManyAsync(indexes);
        _logger.LogInformation("MongoDB schema indexes created successfully");
    }

    /// <inheritdoc />
    public async Task<SchemaEntry?> GetByIdentifierAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync();
        var filter = CreateAccessFilter(identifier, organizationId);
        var doc = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return doc is null ? null : ToSchemaEntry(doc);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<SchemaEntry> Schemas, int TotalCount, string? NextCursor)> ListAsync(
        SchemaCategory? category = null,
        SchemaStatus? status = null,
        string? search = null,
        string? organizationId = null,
        int limit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync();
        var filterBuilder = Builders<MongoSchemaDocument>.Filter;
        var filters = new List<FilterDefinition<MongoSchemaDocument>>();

        // Access control: org-specific + globally published
        if (!string.IsNullOrEmpty(organizationId))
        {
            filters.Add(filterBuilder.Or(
                filterBuilder.Eq(s => s.OrganizationId, organizationId),
                filterBuilder.Eq(s => s.IsGloballyPublished, true)));
        }
        else
        {
            filters.Add(filterBuilder.Eq(s => s.IsGloballyPublished, true));
        }

        // Category filter
        if (category.HasValue)
        {
            filters.Add(filterBuilder.Eq(s => s.Category, category.Value.ToString()));
        }

        // Status filter
        if (status.HasValue)
        {
            filters.Add(filterBuilder.Eq(s => s.Status, status.Value.ToString()));
        }

        // Search filter (title and description)
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Enforce max length to limit resource consumption
            if (search.Length > 200)
            {
                search = search[..200];
            }

            // Escape regex special characters to prevent ReDoS attacks
            var escapedSearch = System.Text.RegularExpressions.Regex.Escape(search);
            var searchRegex = new BsonRegularExpression(escapedSearch, "i");
            filters.Add(filterBuilder.Or(
                filterBuilder.Regex(s => s.Title, searchRegex),
                filterBuilder.Regex(s => s.Description, searchRegex)));
        }

        // Cursor-based pagination
        if (!string.IsNullOrEmpty(cursor))
        {
            filters.Add(filterBuilder.Gt(s => s.Identifier, cursor));
        }

        var combinedFilter = filters.Count > 0
            ? filterBuilder.And(filters)
            : filterBuilder.Empty;

        // Get total count first
        var totalCount = (int)await _collection.CountDocumentsAsync(combinedFilter, cancellationToken: cancellationToken);

        // Get paginated results
        var docs = await _collection.Find(combinedFilter)
            .SortBy(s => s.Identifier)
            .Limit(limit)
            .ToListAsync(cancellationToken);

        var schemas = docs.Select(ToSchemaEntry).ToList().AsReadOnly();
        var nextCursor = docs.Count == limit ? docs.Last().Identifier : null;

        return (schemas, totalCount, nextCursor);
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> CreateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureIndexesAsync();

        var doc = ToDocument(entry);
        try
        {
            await _collection.InsertOneAsync(doc, new InsertOneOptions(), cancellationToken);
            _logger.LogDebug("Created schema {Identifier} for org {OrganizationId}", entry.Identifier, entry.OrganizationId);
            return entry;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException($"Schema with identifier '{entry.Identifier}' already exists.", ex);
        }
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> UpdateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureIndexesAsync();

        entry.DateModified = DateTimeOffset.UtcNow;
        var doc = ToDocument(entry);

        var filter = Builders<MongoSchemaDocument>.Filter.And(
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.Identifier, entry.Identifier),
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.OrganizationId, entry.OrganizationId));

        var result = await _collection.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = false }, cancellationToken);

        if (result.ModifiedCount == 0)
        {
            throw new KeyNotFoundException($"Schema '{entry.Identifier}' not found for organization.");
        }

        _logger.LogDebug("Updated schema {Identifier}", entry.Identifier);
        return entry;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string identifier,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync();
        var filter = Builders<MongoSchemaDocument>.Filter.And(
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.Identifier, identifier),
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.OrganizationId, organizationId));

        var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        var deleted = result.DeletedCount > 0;

        if (deleted)
        {
            _logger.LogDebug("Deleted schema {Identifier} for org {OrganizationId}", identifier, organizationId);
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync();
        var filter = CreateAccessFilter(identifier, organizationId);
        var count = await _collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsGloballyAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexesAsync();
        var filter = Builders<MongoSchemaDocument>.Filter.And(
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.Identifier, identifier),
            Builders<MongoSchemaDocument>.Filter.Eq(s => s.IsGloballyPublished, true));

        var count = await _collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    /// <summary>
    /// Creates a filter for schema access based on identifier and organization.
    /// </summary>
    private static FilterDefinition<MongoSchemaDocument> CreateAccessFilter(string identifier, string? organizationId)
    {
        var filterBuilder = Builders<MongoSchemaDocument>.Filter;
        var identifierFilter = filterBuilder.Eq(s => s.Identifier, identifier);

        // If organizationId is provided, check both org-specific and globally published
        if (!string.IsNullOrEmpty(organizationId))
        {
            return filterBuilder.And(
                identifierFilter,
                filterBuilder.Or(
                    filterBuilder.Eq(s => s.OrganizationId, organizationId),
                    filterBuilder.Eq(s => s.IsGloballyPublished, true)));
        }

        // Without organizationId, only globally published schemas are accessible
        return filterBuilder.And(
            identifierFilter,
            filterBuilder.Eq(s => s.IsGloballyPublished, true));
    }

    /// <summary>
    /// Converts a MongoDB document to a SchemaEntry.
    /// </summary>
    private static SchemaEntry ToSchemaEntry(MongoSchemaDocument doc)
    {
        var jsonContent = doc.Content.ToJson();
        return new SchemaEntry
        {
            Identifier = doc.Identifier,
            Title = doc.Title,
            Description = doc.Description,
            Version = doc.Version,
            Category = Enum.Parse<SchemaCategory>(doc.Category),
            Status = Enum.Parse<SchemaStatus>(doc.Status),
            Source = new SchemaSource
            {
                Type = Enum.Parse<SourceType>(doc.SourceType),
                Uri = doc.SourceUri,
                Provider = doc.SourceProvider
            },
            OrganizationId = doc.OrganizationId,
            IsGloballyPublished = doc.IsGloballyPublished,
            Content = JsonDocument.Parse(jsonContent),
            DateAdded = doc.DateAdded,
            DateModified = doc.DateModified,
            DateDeprecated = doc.DateDeprecated
        };
    }

    /// <summary>
    /// Converts a SchemaEntry to a MongoDB document.
    /// </summary>
    private static MongoSchemaDocument ToDocument(SchemaEntry entry)
    {
        var jsonContent = entry.Content.RootElement.GetRawText();
        return new MongoSchemaDocument
        {
            Id = entry.Identifier,
            Identifier = entry.Identifier,
            Title = entry.Title,
            Description = entry.Description,
            Version = entry.Version,
            Category = entry.Category.ToString(),
            Status = entry.Status.ToString(),
            SourceType = entry.Source.Type.ToString(),
            SourceUri = entry.Source.Uri,
            SourceProvider = entry.Source.Provider,
            OrganizationId = entry.OrganizationId,
            IsGloballyPublished = entry.IsGloballyPublished,
            Content = BsonDocument.Parse(jsonContent),
            DateAdded = entry.DateAdded,
            DateModified = entry.DateModified,
            DateDeprecated = entry.DateDeprecated
        };
    }
}
