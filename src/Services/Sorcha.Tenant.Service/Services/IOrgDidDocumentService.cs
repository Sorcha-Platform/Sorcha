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

    /// <summary>
    /// Regenerates the DID document for the org. Idempotent — returns the existing row
    /// unchanged when the recomputed key-version fingerprint matches the persisted one.
    /// </summary>
    Task<OrgDidDocument> RegenerateAsync(
        Guid organizationId, KeyEventReason reason, CancellationToken ct = default);
}
