// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.OrgInfo;

/// <summary>
/// Cross-service client for resolving organisation metadata from the Tenant Service
/// (Feature 149). Used by the Wallet Service to anchor the VC-issuer DID on the org's
/// canonical operational wallet.
/// </summary>
public interface IOrgInfoClient
{
    /// <summary>
    /// Resolves the organisation's canonical operational wallet address
    /// (<c>Organization.WalletAddress</c>) — the address the rest of the platform uses
    /// for <c>did:sorcha:org:{address}</c> (register ownership, invitations, X.509 SAN,
    /// trust allowlists).
    /// </summary>
    /// <param name="organizationId">The organisation to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The canonical wallet address, or <c>null</c> when the org is unknown or has no
    /// provisioned wallet (404), or on transport/parse failure. A null result means the
    /// caller cannot produce a resolvable issuer identity and MUST fail issuance closed.
    /// </returns>
    Task<string?> ResolveCanonicalWalletAddressAsync(Guid organizationId, CancellationToken ct = default);
}
