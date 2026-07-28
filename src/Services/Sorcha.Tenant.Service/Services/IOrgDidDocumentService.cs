// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Per-organisation DID document service (Feature 120 US2).
/// </summary>
public interface IOrgDidDocumentService
{
    /// <summary>Returns the published DID document for the org, or null if none exists.</summary>
    Task<OrgDidDocument?> GetAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns the published DID document whose canonical <c>PrimaryDid</c> equals the supplied
    /// <c>did:sorcha:org:{walletAddress}</c>, or null if none exists. Lets a verifier resolve the
    /// document from the issuer DID alone (the by-orgId route requires the GUID). Feature 149.
    /// </summary>
    Task<OrgDidDocument?> GetByPrimaryDidAsync(string primaryDid, CancellationToken ct = default);

    // NOTE: there is deliberately NO RegenerateAsync(orgId, reason) here.
    //
    // It existed, was never called, and unconditionally threw NotSupportedException — Tenant
    // holds no key material, so it cannot rebuild a document from an orgId alone. Its presence
    // implied a server-side rebuild capability that does not exist, and the Wallet-side client
    // documented a matching "lazy rebuild will recover" that was equally untrue. Together they
    // made a failed publish look self-healing when it was permanent.
    //
    // Regeneration is snapshot-driven and Wallet-initiated: the key holder POSTs its current
    // active-key snapshot to /orgs/{orgId}/did-document/regenerate
    // (OrgDidDocumentService.RegenerateFromSnapshotAsync, idempotent on the key-version
    // fingerprint). The repair path lives in IssuanceKeyService.EnsureDidDocumentPublishedAsync,
    // which re-ensures publication before every signature and fails closed if it cannot.
}
