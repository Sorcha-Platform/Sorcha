// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Trust;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Resolves the X.509 cert chain for the JWS <c>x5c</c> header on issuance, per the credential's trust
/// anchor (Feature 181 US4). The <c>x509-lotl</c> anchor resolves the org's imported external chain and
/// <b>fails closed</b> when none is valid; <c>x509-tenant</c> (and the pre-181 default) attaches the tenant
/// chain and degrades to DID-only on provider failure; <c>register</c> attaches no chain.
/// </summary>
public static class IssueCredentialChainResolver
{
    /// <summary>
    /// Returns the leaf-first cert chain for <paramref name="issuerWallet"/> under
    /// <paramref name="tenantId"/>, or null when no chain should be embedded.
    /// </summary>
    /// <param name="trustAnchor">
    /// The credential's trust anchor — <c>register</c> | <c>x509-tenant</c> | <c>x509-lotl</c>. Null/unknown
    /// preserves the pre-181 tenant-chain behaviour.
    /// </param>
    public static async Task<IReadOnlyList<byte[]>?> ResolveChainAsync(
        IOrgCertChainProvider? provider,
        string? tenantId,
        string issuerWallet,
        ILogger logger,
        CancellationToken cancellationToken,
        string? trustAnchor = null)
    {
        // register anchor → DID-only, no x5c.
        if (string.Equals(trustAnchor, "register", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (provider is null || string.IsNullOrWhiteSpace(tenantId))
        {
            if (string.Equals(trustAnchor, "x509-lotl", StringComparison.OrdinalIgnoreCase))
            {
                // Fail closed — the external anchor was requested but we cannot resolve any chain.
                throw new ExternalAnchorUnavailableException(tenantId, issuerWallet);
            }
            return null;
        }

        // x509-lotl → the imported external chain ONLY. Never fall back to the tenant root (FR-020).
        if (string.Equals(trustAnchor, "x509-lotl", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<byte[]>? imported;
            try
            {
                imported = await provider.GetImportedChainForAsync(tenantId, issuerWallet, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Imported cert chain fetch failed for tenant {TenantId} issuer {IssuerWallet} on x509-lotl anchor; failing closed.",
                    tenantId, issuerWallet);
                imported = null;
            }

            if (imported is null || imported.Count == 0)
            {
                throw new ExternalAnchorUnavailableException(tenantId, issuerWallet);
            }
            return imported;
        }

        // x509-tenant (and the pre-181 default) → tenant chain; degrade to DID-only on failure (FR-021).
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

/// <summary>
/// Feature 181 US4 — thrown when issuance requests the <c>x509-lotl</c> anchor but no valid imported
/// external certificate exists (absent, expired, or key-mismatched after rotation). Maps to
/// <c>CERT_EXTERNAL_ANCHOR_UNAVAILABLE</c> (FR-020).
/// </summary>
public sealed class ExternalAnchorUnavailableException(string? tenantId, string issuerWallet)
    : InvalidOperationException(
        $"CERT_EXTERNAL_ANCHOR_UNAVAILABLE: no valid imported external certificate for issuer {issuerWallet} under tenant {tenantId ?? "(none)"}.");
