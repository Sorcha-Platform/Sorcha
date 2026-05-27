// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Registry of <see cref="ITrustSourceResolver"/>s keyed by <see cref="TrustSourceKind"/>
/// (feature 135). Mirrors the existing decentralised-identifier resolver registry.
/// </summary>
public interface ITrustResolverRegistry
{
    /// <summary>Registers a resolver for its declared kind, replacing any prior registration.</summary>
    void Register(ITrustSourceResolver resolver);

    /// <summary>Resolves the registered resolver for a kind, or null when none is registered.</summary>
    ITrustSourceResolver? Resolve(TrustSourceKind kind);
}
