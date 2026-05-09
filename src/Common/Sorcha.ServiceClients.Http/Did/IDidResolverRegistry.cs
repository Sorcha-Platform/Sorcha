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
    /// Phase 1 stub delegates to <see cref="ResolveAsync"/> (passthrough). Full
    /// cross-resolution algorithm lands in Feature 120 US4 (T055-T059) per
    /// <c>contracts/did-resolver-registry-contract.md</c>.
    /// </remarks>
    Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string did, CancellationToken ct = default);
}
