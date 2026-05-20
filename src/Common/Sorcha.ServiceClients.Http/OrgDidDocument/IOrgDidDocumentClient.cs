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
    /// key snapshot. Returns silently on success; logs and swallows on failure
    /// (key derivation is the source of truth — DID-doc regeneration is a derived
    /// projection that can be lazily rebuilt later).
    /// </summary>
    Task RegenerateAsync(OrgDidRegenerateRequest request, CancellationToken ct = default);

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
