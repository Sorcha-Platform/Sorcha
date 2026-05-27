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

    /// <summary>
    /// Resolves the primary DID and any DIDs declared in its <c>alsoKnownAs</c> property,
    /// verifies the same verification key material appears in every linked document, and
    /// returns the merged <see cref="DidDocument"/>. Returns <c>null</c> if any link fails
    /// to resolve, or if verification keys diverge across the equivalence chain.
    /// </summary>
    /// <remarks>
    /// Implements the deterministic six-step cross-resolution algorithm in
    /// <c>contracts/did-resolver-registry-contract.md</c> "Resolution algorithm" — primary
    /// resolution, alsoKnownAs walk, key-material intersection, cycle protection, merged
    /// document construction. Cached at the registry layer per
    /// <see cref="DidResolverCache"/>; cross-resolution counters and the
    /// <c>did.resolve.cross</c> span are wired in production.
    /// </remarks>
    Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default);
}
