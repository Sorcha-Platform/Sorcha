// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Resolves the issuer's public signing key for a credential, given the
/// credential's <c>iss</c> claim (Feature 114, verifier hardening seam).
/// Production deployments wire a DID-based resolver here (<c>did:sorcha:org:</c>
/// → tenant register lookup → published verification method); the v1 demo and
/// unit tests use the in-memory <see cref="JwkRegistryIssuerKeyResolver"/>.
/// </summary>
/// <remarks>
/// The validator calls this on every presentation. A null result means
/// "no key available" — what the validator does with that is governed by the
/// caller's <c>RequireIssuerSignature</c> policy (true = reject; false = log
/// warning and accept the holder→device chain alone).
/// </remarks>
public interface IIssuerKeyResolver
{
    /// <summary>
    /// Returns the issuer's public JWK if known, else null. Implementations
    /// MUST treat <paramref name="issuer"/> as opaque (a DID, a URL, anything
    /// the credential carried) and only return keys for issuers they trust.
    /// </summary>
    Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default);
}

/// <summary>
/// Default opt-out resolver. Always returns null — the v1 validator then
/// logs a warning and accepts on the strength of the holder→device chain
/// alone, which is the v1 contract documented on
/// <see cref="VerifiablePresentationValidator"/>.
/// </summary>
public sealed class OptOutIssuerKeyResolver : IIssuerKeyResolver
{
    /// <inheritdoc />
    public Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default)
        => Task.FromResult<JsonElement?>(null);
}

/// <summary>
/// In-memory registry of <c>iss</c> → JWK pairs. Used by tests and by the
/// demo-mint endpoint, which registers the freshly-generated issuer key
/// before returning the minted credential to the wallet.
/// </summary>
public sealed class JwkRegistryIssuerKeyResolver : IIssuerKeyResolver
{
    private readonly ConcurrentDictionary<string, JsonElement> _keys = new(StringComparer.Ordinal);

    /// <summary>Register (or replace) the public JWK for an issuer.</summary>
    public void Register(string issuer, JsonElement publicJwk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        // Clone the JsonElement so callers can dispose their JsonDocument safely.
        _keys[issuer] = publicJwk.Clone();
    }

    /// <inheritdoc />
    public Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default)
        => Task.FromResult(_keys.TryGetValue(issuer, out var jwk) ? jwk : (JsonElement?)null);
}
