// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Wire shape of a credential as delivered from <c>GET /api/v1/wallet/credentials</c>
/// or as a <c>credentials.added</c> entry inside a sync response. The wallet caches
/// the JWT after encrypting it locally with the device-bound content key.
/// </summary>
public sealed record CachedCredentialPayload
{
    /// <summary>Server-assigned credential identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Verifiable Credential Type URI (e.g. Verified Citizen, Assured Identity).</summary>
    public string Vct { get; init; } = string.Empty;

    /// <summary>Compact-serialised SD-JWT VC, including all selective-disclosure salts.</summary>
    public string Jwt { get; init; } = string.Empty;

    /// <summary>Issuer DID, e.g. <c>did:sorcha:org:{walletAddress}</c>.</summary>
    public string IssuerDid { get; init; } = string.Empty;

    /// <summary>UTC issuance time.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>UTC expiry, if any.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Status list URI for revocation, if the credential is revocable.</summary>
    public string? StatusListUri { get; init; }

    /// <summary>Bit position within the credential-type status list.</summary>
    public int? StatusListIndex { get; init; }

    /// <summary>
    /// Issuer-supplied display hints, mirroring the Feature 107 <c>x-review</c>
    /// id-card shape (issuerName, credentialName, colourTheme, etc.).
    /// </summary>
    public JsonObject? DisplayMeta { get; init; }
}
