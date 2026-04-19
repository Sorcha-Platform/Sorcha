// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Trust;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Resolves the X.509 cert chain for the JWS <c>x5c</c> header on HAIP issuance;
/// returns null on any provider failure (degrades to DID-only verifiability).
/// </summary>
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
        catch (OperationCanceledException)
        {
            // Caller cancellation must propagate — never silently issue a
            // credential without x5c when the request was abandoned.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Cert chain fetch failed for tenant {TenantId} issuer {IssuerWallet}; issuing credential without x5c header.",
                tenantId, issuerWallet);
            return null;
        }
    }
}
