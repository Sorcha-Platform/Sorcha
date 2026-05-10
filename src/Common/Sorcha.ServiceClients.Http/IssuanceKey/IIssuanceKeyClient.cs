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
}
