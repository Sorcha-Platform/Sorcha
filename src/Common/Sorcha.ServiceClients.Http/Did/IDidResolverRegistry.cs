// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Registry that delegates DID resolution to method-specific resolvers.
/// </summary>
public interface IDidResolverRegistry
{
    /// <summary>
    /// Resolve a DID by parsing its method and delegating to the registered resolver.
    /// Returns null with a warning log if no resolver is registered for the method.
    /// </summary>
    Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default);

    /// <summary>
    /// Register a method-specific resolver.
    /// </summary>
    void Register(IDidResolver resolver);
}
