// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.IdentityModel.Tokens;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Resolves the signing keys an external identity provider publishes (its JWKS), used to verify
/// the signature of ID tokens received during social login (review M3a). The production
/// implementation fetches + caches the JWKS from the provider's <c>jwks_uri</c> (from
/// <see cref="IdentityProviderConfiguration.JwksUri"/> or discovery) and tolerates key rotation;
/// tests inject a fake returning a known key set.
/// </summary>
public interface IOidcSigningKeyResolver
{
    /// <summary>
    /// Returns the provider's current signing keys for <paramref name="config"/>. Pass
    /// <paramref name="forceRefresh"/> = <c>true</c> to bypass the cache (used once on a <c>kid</c>
    /// miss to tolerate key rotation). Throws if no key location is configured or the keys are
    /// unobtainable — the caller must treat that as fail-closed (reject the exchange).
    /// </summary>
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        IdentityProviderConfiguration config, bool forceRefresh = false, CancellationToken ct = default);
}
