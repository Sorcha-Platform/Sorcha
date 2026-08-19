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
    /// <remarks>
    /// Returns null when the org has no provisioned <c>OrgMasterKey</c> — issuance keys
    /// are derived from the master, so the master must exist before lazy derivation can
    /// run. Callers that hit a null return should treat F120 lazy derivation as
    /// not-yet-applicable for this org and continue without it.
    /// </remarks>
    Task<IssuanceKeyState?> GetOrDeriveAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Publishes the org's issuer DID document from current state. Returns whether one is published.
    /// </summary>
    /// <remarks>
    /// Does NOT wait for the org's canonical wallet to exist. It is provisioned by Tenant's
    /// <c>OrgWalletReconciliationService</c>, a 60-second sweep, so waiting for it is not something a
    /// request can usefully do — an attempt to retry here (#1523) sat for 15s and still lost. The
    /// real answer is for the org's wallet to be created as a deliberate admin step when the org is
    /// set up, which is #1525; until then a brand-new org simply has no document to publish yet, and
    /// issuance is unaffected because it re-ensures before every signature and fails closed.
    /// </remarks>
    Task<bool> PublishDidDocumentAsync(Guid organizationId, CancellationToken ct = default);

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

    /// <summary>
    /// Returns the active issuance key's signing material (decrypted private key + kid +
    /// algorithm + issuer DID), or null when the org has no Active issuance key.
    /// </summary>
    /// <remarks>
    /// Production credential-mint callers use this to swap from wallet-key-signing to
    /// org-issuance-key-signing (Feature 120 kid-swap). Emits the versioned kid form
    /// <c>did:sorcha:org:{addr}#vc-issuance-{rotationIndex}</c> per platform default (D3).
    /// </remarks>
    Task<IssuanceSigningMaterial?> GetActiveSigningMaterialAsync(
        Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns every <see cref="IssuanceKeyState"/> row for an org regardless of status.
    /// Used by the published-DID-document path so callers can decide whether to emit a
    /// revoked key's VM (default: drop from <c>assertionMethod</c> + omit from VM list).
    /// </summary>
    Task<IReadOnlyList<IssuanceKeyState>> ListAllAsync(
        Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Rotates the org's Active issuance key (Feature 120 US6 / T067). Marks the
    /// existing Active row as <c>Rotated</c> and derives a fresh key at the next
    /// rotation index. Triggers DID document regeneration with reason
    /// <c>IssuanceKeyRotated</c>.
    /// </summary>
    Task<IssuanceKeyState?> RotateAsync(
        Guid organizationId, Guid governanceOpId, CancellationToken ct = default);

    /// <summary>
    /// Revokes a specific issuance key by rotation index (Feature 120 US6 / T068).
    /// Marks <c>Status=Revoked</c> with <c>RevokedAt</c>, <c>RevocationReason</c>,
    /// <c>RevokedByGovernanceOpId</c>. Idempotent on already-revoked keys.
    /// Triggers DID document regeneration with reason <c>IssuanceKeyRevoked</c>.
    /// </summary>
    Task<IssuanceKeyState?> RevokeAsync(
        Guid organizationId,
        int rotationIndex,
        string reason,
        Guid governanceOpId,
        CancellationToken ct = default);
}

/// <summary>
/// Decrypted signing material for an org's active VC issuance key (Feature 120 kid-swap).
/// </summary>
/// <param name="OrganizationId">Owning organisation.</param>
/// <param name="IssuerDid">Canonical issuer DID — <c>did:sorcha:org:{addr}</c>.</param>
/// <param name="Kid">JWS <c>kid</c> header value — versioned form <c>did:sorcha:org:{addr}#vc-issuance-{rotationIndex}</c>.</param>
/// <param name="PrivateKey">Decrypted private key bytes — must be wiped by caller after signing.</param>
/// <param name="Algorithm">Signing algorithm string (e.g., <c>ED25519</c>).</param>
/// <param name="RotationIndex">Monotonic rotation counter.</param>
public sealed record IssuanceSigningMaterial(
    Guid OrganizationId,
    string IssuerDid,
    string Kid,
    byte[] PrivateKey,
    string Algorithm,
    int RotationIndex);
