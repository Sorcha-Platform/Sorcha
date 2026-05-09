// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.OrgDidDocument;

/// <summary>
/// Wallet → Tenant payload describing the active issuance keys to publish in
/// the org's DID document (Feature 120 US2). Tenant writes the document
/// verbatim from this snapshot — it does NOT call back to Wallet for keys.
/// </summary>
/// <param name="OrganizationId">Owning organisation.</param>
/// <param name="KeyEventReason">What triggered the regeneration (matches <c>KeyEventReason</c> on the tenant side).</param>
/// <param name="WalletAddress">Org wallet address used for the canonical <c>did:sorcha:org:{addr}</c> identifier.</param>
/// <param name="ActiveKeys">Snapshot of every Active issuance key for the org.</param>
public sealed record OrgDidRegenerateRequest(
    Guid OrganizationId,
    string KeyEventReason,
    string WalletAddress,
    IReadOnlyList<OrgDidActiveKey> ActiveKeys);

/// <summary>Single Active issuance key as seen by Tenant.</summary>
/// <param name="RotationIndex">Monotonic rotation counter; forms the versioned kid suffix.</param>
/// <param name="Algorithm">Wallet algorithm string (e.g. <c>ED25519</c>).</param>
/// <param name="PublicKeyJwk">Pre-built JWK JSON for the public key.</param>
/// <param name="Thumbprint">RFC 7638 SHA-256 base64url thumbprint of the JWK.</param>
public sealed record OrgDidActiveKey(
    int RotationIndex,
    string Algorithm,
    string PublicKeyJwk,
    string Thumbprint);
