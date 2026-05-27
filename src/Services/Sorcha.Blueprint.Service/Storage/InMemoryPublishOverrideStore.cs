// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Storage;

/// <summary>
/// In-memory implementation of <see cref="IPublishOverrideStore"/> (Feature 142).
/// Append-only: records are stored by their unique <see cref="PublishOverride.Id"/>
/// and never overwritten. Convenience-grade for single-node/dev use.
/// </summary>
public class InMemoryPublishOverrideStore : IPublishOverrideStore
{
    private readonly ConcurrentDictionary<Guid, PublishOverride> _overrides = new();

    /// <inheritdoc/>
    public Task<PublishOverride> RecordAsync(PublishOverride publishOverride, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publishOverride);
        if (publishOverride.Id == Guid.Empty)
        {
            publishOverride.Id = Guid.NewGuid();
        }

        // Append-only: each record carries a unique Id, so an existing record is never overwritten.
        _overrides[publishOverride.Id] = publishOverride;
        return Task.FromResult(publishOverride);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PublishOverride>> GetByBlueprintAsync(
        string blueprintId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PublishOverride> results = _overrides.Values
            .Where(o => o.BlueprintId == blueprintId)
            .OrderByDescending(o => o.OverriddenAt)
            .ToList();

        return Task.FromResult(results);
    }
}
