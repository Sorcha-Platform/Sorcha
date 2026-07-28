// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.OrgDidDocument;

/// <summary>
/// Wallet → Tenant cross-service client that triggers DID-document regeneration
/// when a wallet-side key event occurs (Feature 120 US2).
/// </summary>
public interface IOrgDidDocumentClient
{
    /// <summary>
    /// Triggers regeneration of the org's published DID document with the supplied
    /// key snapshot. Idempotent — the Tenant side no-ops when the recomputed
    /// key-version fingerprint is unchanged.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the Tenant Service accepted the snapshot; <c>false</c> on any
    /// transport or non-success response. Never throws.
    /// </returns>
    /// <remarks>
    /// The return value is load-bearing: callers that are about to SIGN with the key
    /// this document publishes MUST fail closed on <c>false</c> unless they can confirm
    /// a correctly-anchored document is already published. There is no background
    /// rebuild — an earlier revision of this contract claimed a "lazy rebuild will
    /// recover" and none existed, so a failed publish left the org permanently
    /// unverifiable while issuance carried on succeeding.
    /// </remarks>
    Task<bool> RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Resolves an organisation's canonical DID by fetching its published W3C DID
    /// document (<c>GET /orgs/{orgId}/did.json</c>) and reading the document's
    /// <c>id</c> — the canonical <c>did:sorcha:org:{walletAddress}</c> identifier
    /// (Spec 5, verifier-DID resolution).
    /// </summary>
    /// <param name="orgId">The organisation whose canonical DID to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The canonical <c>did:sorcha:org:*</c> string, or <c>null</c> when the org has
    /// no published document (404 — never issued a credential), or on transport /
    /// parse failure. Best-effort: this method never throws, so callers can use the
    /// result as a display identity with a safe fallback.
    /// </returns>
    Task<string?> ResolveCanonicalDidAsync(Guid orgId, CancellationToken ct = default);
}
