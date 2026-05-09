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
    /// Regenerates the DID document for the org. Idempotent — returns the existing row
    /// unchanged when the recomputed key-version fingerprint matches the persisted one.
    /// </summary>
    Task<OrgDidDocument> RegenerateAsync(
        Guid organizationId, KeyEventReason reason, CancellationToken ct = default);
}
