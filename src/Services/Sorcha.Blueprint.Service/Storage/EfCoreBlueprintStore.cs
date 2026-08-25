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

        // The owning organisation survives an update that does not mention it. A PUT body carries
        // the document, not the row's ownership, so taking `blueprint.OrganizationId` at face value
        // silently nulls the column on the FIRST save and orphans the draft: every later org-scoped
        // GET and PUT then answers 404 for the org that owns it. OwnerId was already treated as
        // immutable for exactly this reason; OrganizationId was not, and it is the one the reads
        // filter on.
        blueprint.OrganizationId ??= !string.IsNullOrEmpty(entity.OrganizationId)
            ? entity.OrganizationId
            : (string.IsNullOrEmpty(entity.OwnerId) ? null : entity.OwnerId);

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

            // The owning organisation is a property of the ROW, not of the document. Taking it from
            // the serialized content alone loses it the first time a client saves the draft back,
            // because no client echoes `organizationId` in a PUT body — and the org is what every
            // org-scoped read and write is authorised against. Re-attaching it here restores the
            // invariant for reads, for the ownership check in BlueprintService.UpdateAsync, and for
            // any row already orphaned by that path (OwnerId is written once at creation and is
            // never overwritten, so it is the durable fallback).
            if (blueprint is not null && string.IsNullOrEmpty(blueprint.OrganizationId))
            {
                blueprint.OrganizationId = !string.IsNullOrEmpty(entity.OrganizationId)
                    ? entity.OrganizationId
                    : (string.IsNullOrEmpty(entity.OwnerId) ? null : entity.OwnerId);
            }

            return blueprint;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize blueprint {BlueprintId} from entity content", entity.Id);
            return null;
        }
    }
}
