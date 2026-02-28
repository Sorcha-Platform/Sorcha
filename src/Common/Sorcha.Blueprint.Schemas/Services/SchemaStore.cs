// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Schemas.Models;
using Sorcha.Blueprint.Schemas.Observability;
using Sorcha.Blueprint.Schemas.Repositories;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Implementation of schema storage and retrieval operations.
/// </summary>
public sealed class SchemaStore : ISchemaStore
{
    private readonly SystemSchemaLoader _systemSchemaLoader;
    private readonly ISchemaRepository? _repository;
    private readonly ILogger<SchemaStore> _logger;
    private readonly SchemaActivitySource? _activitySource;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaStore"/> class.
    /// </summary>
    public SchemaStore(
        SystemSchemaLoader systemSchemaLoader,
        ILogger<SchemaStore> logger,
        ISchemaRepository? repository = null,
        SchemaActivitySource? activitySource = null)
    {
        _systemSchemaLoader = systemSchemaLoader;
        _repository = repository;
        _logger = logger;
        _activitySource = activitySource;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SchemaEntry>> GetSystemSchemasAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource?.StartGetSystemSchemasActivity();

        var schemas = _systemSchemaLoader.GetSystemSchemas();
        _activitySource?.RecordSuccess(activity, schemas.Count);
        return Task.FromResult(schemas);
    }

    /// <inheritdoc />
    public async Task<SchemaEntry?> GetByIdentifierAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        // Check system schemas first
        var systemSchema = _systemSchemaLoader.GetSystemSchema(identifier);
        if (systemSchema is not null)
        {
            return systemSchema;
        }

        // Check repository for external/custom schemas
        if (_repository is null)
        {
            return null;
        }

        return await _repository.GetByIdentifierAsync(identifier, organizationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<SchemaEntry> Schemas, int TotalCount, string? NextCursor)> ListAsync(
        SchemaCategory? category = null,
        SchemaStatus? status = SchemaStatus.Active,
        string? search = null,
        string? organizationId = null,
        int limit = 50,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        // Collect matching system schemas (small fixed set, always fetched in full)
        var systemSchemas = new List<SchemaEntry>();
        if (category is null or SchemaCategory.System)
        {
            systemSchemas = _systemSchemaLoader.GetSystemSchemas()
                .Where(s => status is null || s.Status == status)
                .Where(s => string.IsNullOrEmpty(search) ||
                           s.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                           (s.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .OrderBy(s => s.Title)
                .ToList();
        }

        // System schemas only appear on the first page (no cursor).
        // When a cursor is present we are paginating through repository results only.
        var includeSystem = cursor is null;

        // Determine how many system schemas fit on this page and how many repo slots remain
        var systemForPage = includeSystem ? Math.Min(systemSchemas.Count, limit) : 0;
        var repoLimit = limit - systemForPage;

        // Fetch repository schemas with cursor-based pagination
        int repoTotalCount = 0;
        string? repoNextCursor = null;
        var repoResults = new List<SchemaEntry>();

        if (_repository is not null && category != SchemaCategory.System && repoLimit > 0)
        {
            var repoSchemas = await _repository.ListAsync(
                category,
                status,
                search,
                organizationId,
                repoLimit,
                cursor,
                cancellationToken);

            repoResults.AddRange(repoSchemas.Schemas);
            repoTotalCount = repoSchemas.TotalCount;
            repoNextCursor = repoSchemas.NextCursor;
        }
        else if (_repository is not null && category != SchemaCategory.System && repoLimit == 0)
        {
            // System schemas filled the page; still need repo total count and
            // a cursor so the caller knows there may be more results.
            var repoSchemas = await _repository.ListAsync(
                category,
                status,
                search,
                organizationId,
                1,
                cursor,
                cancellationToken);

            repoTotalCount = repoSchemas.TotalCount;

            // If the repository has any items, use an empty-string cursor to
            // signal "start from the beginning" on the next page.
            if (repoTotalCount > 0)
            {
                repoNextCursor = repoSchemas.Schemas.Count > 0
                    ? repoSchemas.Schemas[0].Identifier
                    : null;
            }
        }

        // Combine: system schemas first (first page only), then repository results
        var results = new List<SchemaEntry>();
        if (includeSystem)
        {
            results.AddRange(systemSchemas.Take(systemForPage));
        }

        results.AddRange(repoResults);

        // Total count = all matching system schemas + all matching repo schemas
        var totalCount = systemSchemas.Count + repoTotalCount;

        // Use the repository cursor for pagination continuity
        return (results.AsReadOnly(), totalCount, repoNextCursor);
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> CreateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource?.StartCreateActivity(
            entry.Identifier,
            entry.Category.ToString(),
            entry.OrganizationId);

        try
        {
            if (entry.Category != SchemaCategory.Custom)
            {
                throw new InvalidOperationException("Only Custom schemas can be created.");
            }

            if (SystemSchemaLoader.IsSystemSchema(entry.Identifier))
            {
                throw new InvalidOperationException($"Identifier '{entry.Identifier}' conflicts with system schema.");
            }

            if (_repository is null)
            {
                throw new InvalidOperationException("Schema repository is not configured.");
            }

            if (await _repository.ExistsAsync(entry.Identifier, entry.OrganizationId, cancellationToken))
            {
                throw new InvalidOperationException($"Schema with identifier '{entry.Identifier}' already exists.");
            }

            _logger.LogInformation("Creating custom schema: {Identifier}", entry.Identifier);
            var result = await _repository.CreateAsync(entry, cancellationToken);
            _activitySource?.RecordSuccess(activity);
            return result;
        }
        catch (Exception ex)
        {
            _activitySource?.RecordFailure(activity, ex);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> UpdateAsync(SchemaEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Category != SchemaCategory.Custom)
        {
            throw new InvalidOperationException("Only Custom schemas can be updated.");
        }

        if (_repository is null)
        {
            throw new InvalidOperationException("Schema repository is not configured.");
        }

        _logger.LogInformation("Updating custom schema: {Identifier}", entry.Identifier);
        return await _repository.UpdateAsync(entry, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string identifier,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        if (SystemSchemaLoader.IsSystemSchema(identifier))
        {
            throw new InvalidOperationException("System schemas cannot be deleted.");
        }

        if (_repository is null)
        {
            throw new InvalidOperationException("Schema repository is not configured.");
        }

        var schema = await _repository.GetByIdentifierAsync(identifier, organizationId, cancellationToken);
        if (schema is null)
        {
            return false;
        }

        if (schema.Category != SchemaCategory.Custom)
        {
            throw new InvalidOperationException("Only Custom schemas can be deleted.");
        }

        _logger.LogInformation("Deleting custom schema: {Identifier}", identifier);
        return await _repository.DeleteAsync(identifier, organizationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> DeprecateAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var schema = await GetByIdentifierAsync(identifier, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Schema '{identifier}' not found.");

        if (schema.Category == SchemaCategory.System)
        {
            throw new InvalidOperationException("System schemas cannot be deprecated.");
        }

        schema.Deprecate();

        if (_repository is not null && schema.Category != SchemaCategory.System)
        {
            await _repository.UpdateAsync(schema, cancellationToken);
        }

        _logger.LogInformation("Deprecated schema: {Identifier}", identifier);
        return schema;
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> ActivateAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var schema = await GetByIdentifierAsync(identifier, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Schema '{identifier}' not found.");

        schema.Activate();

        if (_repository is not null && schema.Category != SchemaCategory.System)
        {
            await _repository.UpdateAsync(schema, cancellationToken);
        }

        _logger.LogInformation("Activated schema: {Identifier}", identifier);
        return schema;
    }

    /// <inheritdoc />
    public async Task<SchemaEntry> PublishGloballyAsync(
        string identifier,
        string organizationId,
        CancellationToken cancellationToken = default)
    {
        if (_repository is null)
        {
            throw new InvalidOperationException("Schema repository is not configured.");
        }

        var schema = await _repository.GetByIdentifierAsync(identifier, organizationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Schema '{identifier}' not found.");

        if (schema.Category != SchemaCategory.Custom)
        {
            throw new InvalidOperationException("Only Custom schemas can be published globally.");
        }

        // Check for conflicts with global schemas
        if (await ExistsGloballyAsync(identifier, cancellationToken))
        {
            throw new InvalidOperationException($"Identifier '{identifier}' conflicts with existing global schema.");
        }

        schema.PublishGlobally();
        await _repository.UpdateAsync(schema, cancellationToken);

        _logger.LogInformation("Published schema globally: {Identifier}", identifier);
        return schema;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        string identifier,
        string? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (SystemSchemaLoader.IsSystemSchema(identifier))
        {
            return true;
        }

        if (_repository is null)
        {
            return false;
        }

        return await _repository.ExistsAsync(identifier, organizationId, cancellationToken);
    }

    private async Task<bool> ExistsGloballyAsync(string identifier, CancellationToken cancellationToken)
    {
        if (SystemSchemaLoader.IsSystemSchema(identifier))
        {
            return true;
        }

        if (_repository is null)
        {
            return false;
        }

        return await _repository.ExistsGloballyAsync(identifier, cancellationToken);
    }
}
