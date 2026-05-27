// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Data;
using Sorcha.Blueprint.Service.Data.Entities;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// EF Core implementation of <see cref="IRehearsalPassStore"/> (Feature 142).
/// Registered as a singleton; uses <see cref="IDbContextFactory{TContext}"/> to
/// create scoped <see cref="BlueprintDbContext"/> instances per operation.
/// Passes are insert-only; the latest per <c>(BlueprintId, ExecDefHash)</c> is
/// resolved by <see cref="RehearsalPassEntity.RehearsedAt"/>.
/// </summary>
public class EfCoreRehearsalPassStore : IRehearsalPassStore
{
    private readonly IDbContextFactory<BlueprintDbContext> _contextFactory;
    private readonly ILogger<EfCoreRehearsalPassStore> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EfCoreRehearsalPassStore"/>.
    /// </summary>
    /// <param name="contextFactory">Factory for creating scoped database contexts.</param>
    /// <param name="logger">Logger instance.</param>
    public EfCoreRehearsalPassStore(
        IDbContextFactory<BlueprintDbContext> contextFactory,
        ILogger<EfCoreRehearsalPassStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RehearsalPass> RecordAsync(RehearsalPass pass, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Id == Guid.Empty)
        {
            pass.Id = Guid.NewGuid();
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.RehearsalPasses.Add(ToEntity(pass));
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded rehearsal pass {PassId} for blueprint {BlueprintId} (execDefHash {ExecDefHash})",
            pass.Id, pass.BlueprintId, pass.ExecDefHash);

        return pass;
    }

    /// <inheritdoc/>
    public async Task<RehearsalPass?> GetLatestAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.RehearsalPasses.AsNoTracking()
            .Where(p => p.BlueprintId == blueprintId && p.ExecDefHash == execDefHash)
            .OrderByDescending(p => p.RehearsedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ToModel(entity);
    }

    private static RehearsalPassEntity ToEntity(RehearsalPass pass) => new()
    {
        Id = pass.Id,
        BlueprintId = pass.BlueprintId,
        ExecDefHash = pass.ExecDefHash,
        RehearsedAt = pass.RehearsedAt,
        RehearsedByPlatformUserId = pass.RehearsedByPlatformUserId,
        SandboxRegisterId = pass.SandboxRegisterId
    };

    private static RehearsalPass ToModel(RehearsalPassEntity entity) => new()
    {
        Id = entity.Id,
        BlueprintId = entity.BlueprintId,
        ExecDefHash = entity.ExecDefHash,
        RehearsedAt = entity.RehearsedAt,
        RehearsedByPlatformUserId = entity.RehearsedByPlatformUserId,
        SandboxRegisterId = entity.SandboxRegisterId
    };
}
