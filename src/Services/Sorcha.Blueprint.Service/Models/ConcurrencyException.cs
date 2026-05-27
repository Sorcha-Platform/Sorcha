// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Thrown when a concurrent modification is detected during an optimistic concurrency check.
/// Callers should retry the operation with a fresh read of the entity.
/// </summary>
public class ConcurrencyException : InvalidOperationException
{
    public ConcurrencyException(string entityId, int expectedVersion, int actualVersion)
        : base($"Concurrent modification detected for '{entityId}'. Expected version {expectedVersion}, but found {actualVersion}.")
    {
        EntityId = entityId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>Identifier of the entity.</summary>
    public string EntityId { get; }
    /// <summary>Numeric value for expected version.</summary>
    public int ExpectedVersion { get; }
    /// <summary>Numeric value for actual version.</summary>
    public int ActualVersion { get; }
}
