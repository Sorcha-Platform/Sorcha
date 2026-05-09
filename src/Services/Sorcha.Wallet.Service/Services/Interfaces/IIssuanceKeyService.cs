// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Per-organisation VC issuance key lifecycle (Feature 120 US2).
/// </summary>
/// <remarks>
/// <para>v1 surface: lazy derivation + lookup. Rotation and revocation land in US6.</para>
/// <para>Underpinned by the existing org-key derivation infrastructure (Feature 083);
/// this service adds the issuance-specific lifecycle row + thumbprint + DID document
/// regeneration trigger.</para>
/// </remarks>
public interface IIssuanceKeyService
{
    /// <summary>
    /// Returns the active issuance key for the org, deriving it on first call. Idempotent.
    /// </summary>
    Task<IssuanceKeyState> GetOrDeriveAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns the currently-active issuance key for the org, or null if none has been derived.
    /// </summary>
    Task<IssuanceKeyState?> GetActiveAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns the public JWK for the issuance key at <paramref name="rotationIndex"/>, or null
    /// if no row exists. Used by the kid-resolution path to map versioned + thumbprint kid forms.
    /// </summary>
    Task<System.Text.Json.JsonElement?> GetPublicJwkAsync(
        Guid organizationId, int rotationIndex, CancellationToken ct = default);
}
