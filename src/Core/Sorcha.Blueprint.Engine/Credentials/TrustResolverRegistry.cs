// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Default <see cref="ITrustResolverRegistry"/> (feature 135). Holds one resolver per
/// <see cref="TrustSourceKind"/>; the last registration for a kind wins. Mirrors the
/// decentralised-identifier resolver registry.
/// </summary>
public class TrustResolverRegistry : ITrustResolverRegistry
{
    private readonly Dictionary<TrustSourceKind, ITrustSourceResolver> _resolvers = new();

    /// <summary>Creates an empty registry.</summary>
    public TrustResolverRegistry() { }

    /// <summary>Creates a registry pre-populated with the supplied resolvers.</summary>
    public TrustResolverRegistry(IEnumerable<ITrustSourceResolver> resolvers)
    {
        ArgumentNullException.ThrowIfNull(resolvers);
        foreach (var resolver in resolvers)
            Register(resolver);
    }

    /// <inheritdoc />
    public void Register(ITrustSourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolvers[resolver.Kind] = resolver;
    }

    /// <inheritdoc />
    public ITrustSourceResolver? Resolve(TrustSourceKind kind)
        => _resolvers.TryGetValue(kind, out var resolver) ? resolver : null;
}
