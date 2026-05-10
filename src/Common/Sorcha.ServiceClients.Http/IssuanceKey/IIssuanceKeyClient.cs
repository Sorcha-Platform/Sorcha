// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.IssuanceKey;

/// <summary>
/// Cross-service trigger for the wallet's lazy VC-issuance-key derivation
/// (Feature 120 T039). Used by services that mint credentials outside the
/// direct <c>/credentials/issue</c> path (notably <c>Sorcha.Haip.Service</c>'s
/// pre-authorized_code flow) to honor FR-004 'no later than first issuance'.
/// </summary>
public interface IIssuanceKeyClient
{
    /// <summary>
    /// Idempotently derives (or returns the existing) Active issuance key for the org
    /// and triggers DID document publish on the Tenant side. Logs and swallows on
    /// failure — minting a credential should not be blocked by a transient
    /// Wallet-side derivation failure.
    /// </summary>
    Task EnsureAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Sign-on-behalf — produces a signature using the org's Active issuance private
    /// key without transmitting key material across services. Returns null when no
    /// Active issuance key exists (caller should fall back to local signing).
    /// </summary>
    /// <param name="organizationId">Owning organisation.</param>
    /// <param name="dataToSign">The bytes to sign — typically the JWS signing input <c>header.payload</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IssuanceSignResult?> SignAsync(
        Guid organizationId, byte[] dataToSign, CancellationToken ct = default);
}

/// <summary>Result of a sign-on-behalf call (Feature 120 HAIP kid-swap).</summary>
/// <param name="Signature">Raw signature bytes.</param>
/// <param name="Kid">JWS <c>kid</c> header value to embed.</param>
/// <param name="IssuerDid">Canonical issuer DID — set as the JWS <c>iss</c> claim.</param>
/// <param name="Algorithm">Signing algorithm used (e.g., <c>EdDSA</c>).</param>
/// <param name="RotationIndex">Monotonic rotation counter of the signing key.</param>
public sealed record IssuanceSignResult(
    byte[] Signature,
    string Kid,
    string IssuerDid,
    string Algorithm,
    int RotationIndex);
