// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// In-memory implementation of <see cref="IRehearsalPassStore"/> (Feature 142).
/// Convenience-grade store for single-node/dev use; passes are appended and the
/// latest per <c>(BlueprintId, ExecDefHash)</c> is resolved by
/// <see cref="RehearsalPass.RehearsedAt"/>.
/// </summary>
public class InMemoryRehearsalPassStore : IRehearsalPassStore
{
    private readonly ConcurrentDictionary<Guid, RehearsalPass> _passes = new();

    /// <inheritdoc/>
    public Task<RehearsalPass> RecordAsync(RehearsalPass pass, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (pass.Id == Guid.Empty)
        {
            pass.Id = Guid.NewGuid();
        }

        _passes[pass.Id] = pass;
        return Task.FromResult(pass);
    }

    /// <inheritdoc/>
    public Task<RehearsalPass?> GetLatestAsync(
        string blueprintId,
        string execDefHash,
        CancellationToken cancellationToken = default)
    {
        var latest = _passes.Values
            .Where(p => p.BlueprintId == blueprintId && p.ExecDefHash == execDefHash)
            .OrderByDescending(p => p.RehearsedAt)
            .FirstOrDefault();

        return Task.FromResult(latest);
    }
}
