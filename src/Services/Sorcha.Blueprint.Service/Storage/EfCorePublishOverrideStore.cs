// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IPublishOverrideStore"/> (Feature 142).
/// Registered as a singleton; uses <see cref="IDbContextFactory{TContext}"/> to
/// create scoped <see cref="BlueprintDbContext"/> instances per operation.
/// Append-only audit: records are inserted and never mutated or deleted.
/// </summary>
public class EfCorePublishOverrideStore : IPublishOverrideStore
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCorePublishOverrideStore> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EfCorePublishOverrideStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    public EfCorePublishOverrideStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCorePublishOverrideStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PublishOverride> RecordAsync(PublishOverride publishOverride, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishOverride);
        if (publishOverride.Id == Guid.Empty)
        {
            publishOverride.Id = Guid.NewGuid();
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.PublishOverrides.Add(ToEntity(publishOverride));
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded publish override {OverrideId} for blueprint {BlueprintId} v{Version} on register {RegisterId}",
            publishOverride.Id, publishOverride.BlueprintId, publishOverride.Version, publishOverride.RegisterId);

        return publishOverride;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PublishOverride>> GetByBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.PublishOverrides.AsNoTracking()
            .Where(o => o.BlueprintId == blueprintId)
            .OrderByDescending(o => o.OverriddenAt)
            .ToListAsync(cancellationToken);

        return entities.Select(ToModel).ToList();
    }

    private static PublishOverrideEntity ToEntity(PublishOverride model) => new()
    {
        Id = model.Id,
        BlueprintId = model.BlueprintId,
        Version = model.Version,
        RegisterId = model.RegisterId,
        ExecDefHash = model.ExecDefHash,
        OverriddenByPlatformUserId = model.OverriddenByPlatformUserId,
        OverriddenAt = model.OverriddenAt,
        Reason = model.Reason
    };

    private static PublishOverride ToModel(PublishOverrideEntity entity) => new()
    {
        Id = entity.Id,
        BlueprintId = entity.BlueprintId,
        Version = entity.Version,
        RegisterId = entity.RegisterId,
        ExecDefHash = entity.ExecDefHash,
        OverriddenByPlatformUserId = entity.OverriddenByPlatformUserId,
        OverriddenAt = entity.OverriddenAt,
        Reason = entity.Reason
    };
}
