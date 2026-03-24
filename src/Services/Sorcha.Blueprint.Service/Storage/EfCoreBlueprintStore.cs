// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IBlueprintStore"/>.
/// Registered as a singleton; uses <see cref="IDbContextFactory{TContext}"/>
/// to create scoped <see cref="BlueprintDbContext"/> instances per operation.
/// </summary>
public class EfCoreBlueprintStore : IBlueprintStore
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCoreBlueprintStore> _logger;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreBlueprintStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    public EfCoreBlueprintStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreBlueprintStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> GetAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.BlueprintDrafts.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id);

        if (entity is null)
        {
            return null;
        }

        return DeserializeBlueprint(entity);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<BlueprintModel>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.BlueprintDrafts.AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();

        return entities.Select(DeserializeBlueprint).Where(b => b is not null).Cast<BlueprintModel>();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<BlueprintModel>> GetAllByOrgAsync(string organizationId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entities = await context.BlueprintDrafts.AsNoTracking()
            .Where(d => d.OrganizationId == organizationId)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();

        return entities.Select(DeserializeBlueprint).Where(b => b is not null).Cast<BlueprintModel>();
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel> AddAsync(BlueprintModel blueprint)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var now = DateTimeOffset.UtcNow;
        var entity = new BlueprintDraftEntity
        {
            Id = blueprint.Id,
            OwnerId = blueprint.OrganizationId ?? string.Empty,
            Name = blueprint.Title,
            Description = blueprint.Description,
            Content = JsonSerializer.Serialize(blueprint, SerializerOptions),
            OrganizationId = blueprint.OrganizationId,
            Status = DraftStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.BlueprintDrafts.Add(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation("Stored blueprint {BlueprintId} for org {OrgId}", blueprint.Id, blueprint.OrganizationId);

        return blueprint;
    }

    /// <inheritdoc/>
    public async Task<BlueprintModel?> UpdateAsync(string id, BlueprintModel blueprint)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.BlueprintDrafts.FirstOrDefaultAsync(d => d.Id == id);
        if (entity is null)
        {
            return null;
        }

        entity.Name = blueprint.Title;
        entity.Description = blueprint.Description;
        entity.Content = JsonSerializer.Serialize(blueprint, SerializerOptions);
        entity.OrganizationId = blueprint.OrganizationId;
        // OwnerId is immutable after creation — never overwrite on update
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        _logger.LogInformation("Updated blueprint {BlueprintId}", id);

        return blueprint;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(string id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var entity = await context.BlueprintDrafts.FirstOrDefaultAsync(d => d.Id == id);
        if (entity is null)
        {
            return false;
        }

        context.BlueprintDrafts.Remove(entity);
        await context.SaveChangesAsync();

        _logger.LogInformation("Deleted blueprint {BlueprintId}", id);

        return true;
    }

    private BlueprintModel? DeserializeBlueprint(BlueprintDraftEntity entity)
    {
        try
        {
            var blueprint = JsonSerializer.Deserialize<BlueprintModel>(entity.Content, SerializerOptions);
            return blueprint;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize blueprint {BlueprintId} from entity content", entity.Id);
            return null;
        }
    }
}
