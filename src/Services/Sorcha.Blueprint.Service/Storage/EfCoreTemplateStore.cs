// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using Sorcha.Storage.Abstractions;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IDocumentStore{TDocument, TId}"/> for
/// <see cref="BlueprintTemplate"/>. Registered as a singleton; uses
/// <see cref="IDbContextFactory{TContext}"/> to create scoped contexts per operation.
/// </summary>
public class EfCoreTemplateStore : IDocumentStore<BlueprintTemplate, string>
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCoreTemplateStore> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreTemplateStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    public EfCoreTemplateStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreTemplateStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BlueprintTemplate?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.BlueprintTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        return entity is null ? null : DeserializeTemplate(entity);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<BlueprintTemplate>> GetManyAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var idList = ids.ToList();

        var entities = await context.BlueprintTemplates.AsNoTracking()
            .Where(t => idList.Contains(t.Id))
            .ToListAsync(cancellationToken);

        return entities
            .Select(DeserializeTemplate)
            .Where(t => t is not null)
            .Cast<BlueprintTemplate>();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<BlueprintTemplate>> QueryAsync(
        Expression<Func<BlueprintTemplate, bool>> filter,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Load all templates and apply the filter in-memory since the filter
        // expression targets BlueprintTemplate, not the entity type.
        var entities = await context.BlueprintTemplates.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(cancellationToken);

        var compiledFilter = filter.Compile();

        var query = entities
            .Select(DeserializeTemplate)
            .Where(t => t is not null)
            .Cast<BlueprintTemplate>()
            .Where(compiledFilter);

        if (skip.HasValue)
        {
            query = query.Skip(skip.Value);
        }

        if (limit.HasValue)
        {
            query = query.Take(limit.Value);
        }

        return query.ToList();
    }

    /// <inheritdoc/>
    public async Task<BlueprintTemplate> InsertAsync(
        BlueprintTemplate document,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = ToEntity(document);
        context.BlueprintTemplates.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Inserted template {TemplateId}", document.Id);

        return document;
    }

    /// <inheritdoc/>
    public async Task InsertManyAsync(
        IEnumerable<BlueprintTemplate> documents,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = documents.Select(ToEntity).ToList();
        context.BlueprintTemplates.AddRange(entities);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Inserted {Count} templates", entities.Count);
    }

    /// <inheritdoc/>
    public async Task<BlueprintTemplate> ReplaceAsync(
        string id,
        BlueprintTemplate document,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.BlueprintTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new InvalidOperationException($"Template {id} not found for replacement");
        }

        UpdateEntity(entity, document);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Replaced template {TemplateId}", id);

        return document;
    }

    /// <inheritdoc/>
    public async Task<BlueprintTemplate> UpsertAsync(
        string id,
        BlueprintTemplate document,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.BlueprintTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            entity = ToEntity(document);
            entity.Id = id;
            context.BlueprintTemplates.Add(entity);
            _logger.LogInformation("Upserted (inserted) template {TemplateId}", id);
        }
        else
        {
            UpdateEntity(entity, document);
            _logger.LogInformation("Upserted (updated) template {TemplateId}", id);
        }

        await context.SaveChangesAsync(cancellationToken);

        return document;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.BlueprintTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        context.BlueprintTemplates.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted template {TemplateId}", id);

        return true;
    }

    /// <inheritdoc/>
    public async Task<long> DeleteManyAsync(
        Expression<Func<BlueprintTemplate, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.BlueprintTemplates.AsNoTracking()
            .ToListAsync(cancellationToken);

        var compiledFilter = filter.Compile();
        var toDelete = entities
            .Where(e =>
            {
                var template = DeserializeTemplate(e);
                return template is not null && compiledFilter(template);
            })
            .ToList();

        if (toDelete.Count == 0)
        {
            return 0;
        }

        context.BlueprintTemplates.RemoveRange(toDelete);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted {Count} templates", toDelete.Count);

        return toDelete.Count;
    }

    /// <inheritdoc/>
    public async Task<long> CountAsync(
        Expression<Func<BlueprintTemplate, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        if (filter is null)
        {
            return await context.BlueprintTemplates.LongCountAsync(cancellationToken);
        }

        // Apply filter in-memory since it targets BlueprintTemplate
        var entities = await context.BlueprintTemplates.AsNoTracking()
            .ToListAsync(cancellationToken);

        var compiledFilter = filter.Compile();

        return entities
            .Select(DeserializeTemplate)
            .Where(t => t is not null)
            .Cast<BlueprintTemplate>()
            .Count(compiledFilter);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.BlueprintTemplates.AnyAsync(t => t.Id == id, cancellationToken);
    }

    private static BlueprintTemplateEntity ToEntity(BlueprintTemplate template)
    {
        var now = DateTimeOffset.UtcNow;
        return new BlueprintTemplateEntity
        {
            Id = template.Id,
            Name = template.Title,
            Description = template.Description,
            Category = template.Category,
            Content = JsonSerializer.Serialize(template, SerializerOptions),
            Version = template.Version,
            Source = TemplateSource.UserCreated,
            Published = template.Published,
            UsageCount = 0,
            CreatedAt = template.CreatedAt != default ? template.CreatedAt : now,
            UpdatedAt = now
        };
    }

    private static void UpdateEntity(BlueprintTemplateEntity entity, BlueprintTemplate template)
    {
        entity.Name = template.Title;
        entity.Description = template.Description;
        entity.Category = template.Category;
        entity.Content = JsonSerializer.Serialize(template, SerializerOptions);
        entity.Version = template.Version;
        entity.Published = template.Published;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private BlueprintTemplate? DeserializeTemplate(BlueprintTemplateEntity entity)
    {
        try
        {
            var template = JsonSerializer.Deserialize<BlueprintTemplate>(entity.Content, SerializerOptions);
            return template;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize template {TemplateId} from entity content", entity.Id);
            return null;
        }
    }
}
