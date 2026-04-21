// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Register.Core.LocalRelationship;

/// <summary>
/// Derives and caches <see cref="RegisterLocalRelationship"/> per register (Feature 108).
/// Reads the latest sealed <c>RegisterControlRecord</c> plus the local identity to compute
/// role membership. Cache is invalidated on control-transaction seal.
/// </summary>
public interface IRegisterLocalRelationshipService
{
    /// <summary>
    /// Return the local relationship for the register, computing and caching on first call.
    /// Returns <c>null</c> when the register is not known locally or its genesis is not readable.
    /// </summary>
    Task<RegisterLocalRelationship?> DeriveAsync(string registerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force the next <see cref="DeriveAsync"/> call to recompute — called when a control
    /// transaction has just been sealed locally for this register.
    /// </summary>
    void Invalidate(string registerId);

    /// <summary>
    /// Return local relationships for all registers known to this node. Used by
    /// <c>GET /api/internal/my-validated-registers</c> to filter by <c>IsValidator</c>.
    /// </summary>
    Task<IReadOnlyList<RegisterLocalRelationship>> DeriveAllAsync(
        byte[]? validatorPublicKeyOverride = null,
        CancellationToken cancellationToken = default);
}
