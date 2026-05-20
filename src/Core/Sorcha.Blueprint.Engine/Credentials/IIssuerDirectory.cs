// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// What the issuer directory knows about a resolved issuer (feature 135). Engine-local
/// projection of a decentralised-identifier document — just enough for the register and
/// did-allowlist trust sources to make a decision without the Engine taking a dependency
/// on the resolver registry.
/// </summary>
public class IssuerDirectoryEntry
{
    /// <summary>Whether the issuer identifier resolved to a directory entry at all.</summary>
    public bool Resolved { get; set; }

    /// <summary>The verification-method ids currently authorised for assertion (assertionMethod).</summary>
    public IReadOnlyList<string> AssertionMethodKeyIds { get; set; } = [];

    /// <summary>Equivalent issuer identifiers (alsoKnownAs), if any.</summary>
    public IReadOnlyList<string> AlsoKnownAs { get; set; } = [];

    /// <summary>Register height at which the entry was read, when known (for evidence).</summary>
    public long? RegisterHeight { get; set; }
}

/// <summary>
/// Engine-local seam for resolving issuer directory entries (feature 135). Service-layer
/// adapters implement this over <c>IDidResolverRegistry</c>; the Engine ships an in-memory
/// variant so verification (and offline-pinned re-evaluation) runs without network access —
/// mirroring the <see cref="IRevocationChecker"/> pattern.
/// </summary>
public interface IIssuerDirectory
{
    /// <summary>Looks up directory information for an issuer; returns an unresolved entry when unknown.</summary>
    Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken cancellationToken = default);
}
