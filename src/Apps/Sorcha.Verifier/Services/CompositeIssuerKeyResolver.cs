// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Verifier.Services;

/// <summary>
/// Composite resolver that tries each underlying <see cref="IIssuerKeyResolver"/>
/// in order and returns the first non-null result. Used in the production
/// wiring to layer the in-memory JWK registry (demo-mint test fixtures)
/// behind the DID-resolver-backed primary so dev flows keep working without
/// loosening the production failure-mode classification.
/// </summary>
public sealed class CompositeIssuerKeyResolver : IIssuerKeyResolver
{
    private readonly IReadOnlyList<IIssuerKeyResolver> _resolvers;

    /// <summary>DI-friendly constructor. Order of <paramref name="resolvers"/> is significant.</summary>
    public CompositeIssuerKeyResolver(IEnumerable<IIssuerKeyResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        _resolvers = [.. resolvers];
    }

    /// <inheritdoc />
    public async Task<JsonElement?> ResolveAsync(string issuer, CancellationToken ct = default)
    {
        foreach (var r in _resolvers)
        {
            var result = await r.ResolveAsync(issuer, ct).ConfigureAwait(false);
            if (result is not null) return result;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<JsonElement?> ResolveAsync(string issuer, string? kid, CancellationToken ct = default)
    {
        foreach (var r in _resolvers)
        {
            var result = await r.ResolveAsync(issuer, kid, ct).ConfigureAwait(false);
            if (result is not null) return result;
        }
        return null;
    }
}
