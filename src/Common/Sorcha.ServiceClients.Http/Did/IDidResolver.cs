// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves a specific DID method (e.g., "sorcha", "web", "key") to a DID Document.
/// </summary>
public interface IDidResolver
{
    /// <summary>
    /// Resolve a DID to its DID Document.
    /// Returns null if the DID is not found or resolution fails.
    /// </summary>
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);

    /// <summary>
    /// Returns true if this resolver handles the given DID method string.
    /// </summary>
    bool CanResolve(string didMethod);
}
