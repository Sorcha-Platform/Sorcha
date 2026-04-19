// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Trust;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Feature 096 US3 — resolves the X.509 certificate chain for the JWS <c>x5c</c>
/// header on HAIP-path credential issuance.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>CredentialEndpoints.IssueCredential</c> so the chain-fetch
/// decision rules (no provider → null, blank tenant → null, provider failure →
/// null) can be tested in isolation and shared with future issuance paths.
/// </para>
/// <para>
/// The endpoint must never fail credential issuance because the trust client
/// failed — the absence of an <c>x5c</c> header degrades the credential to
/// DID-only verifiability, which is the documented Sorcha-internal default. The
/// resolver therefore swallows provider exceptions and returns null.
/// </para>
/// </remarks>
public static class IssueCredentialChainResolver
{
    /// <summary>
    /// Returns the leaf-first cert chain for <paramref name="issuerWallet"/> under
    /// <paramref name="tenantId"/>, or null when the chain should not be embedded.
    /// </summary>
    public static async Task<IReadOnlyList<byte[]>?> ResolveChainAsync(
        IOrgCertChainProvider? provider,
        string? tenantId,
        string issuerWallet,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        try
        {
            var chain = await provider.GetChainForAsync(tenantId, issuerWallet, cancellationToken);
            return chain?.AsJwsChain();
        }
        catch (Exception ex)
        {
            // Issuance must not fail because of a trust-service hiccup. The
            // missing x5c header degrades the credential to DID-only verifiability.
            logger.LogWarning(ex,
                "Cert chain fetch failed for tenant {TenantId} issuer {IssuerWallet}; issuing credential without x5c header.",
                tenantId, issuerWallet);
            return null;
        }
    }
}
