// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Core.LocalRelationship;

/// <summary>
/// Resolves this installation's identity for deriving per-register role membership (Feature 108).
/// Implementations return the set of wallet addresses the node operates (used to match
/// attestation subjects such as Owner / Admin / Auditor / Designer) and, when available,
/// the local validator's public key (used to match roster entries).
/// </summary>
/// <remarks>
/// The identity is static for the process lifetime by default; implementations may reload
/// on explicit invalidation. The default implementation reads from configuration so dev
/// stacks and tests can pin a deterministic identity without a hard dependency on
/// Wallet.Service being up when Register.Service starts.
/// </remarks>
public interface ILocalIdentityProvider
{
    /// <summary>
    /// Snapshot of the node's local identity. Returns the same object across calls unless
    /// <see cref="Invalidate"/> has been called.
    /// </summary>
    ValueTask<LocalIdentitySnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Force the next <see cref="GetAsync"/> call to re-resolve.</summary>
    void Invalidate();
}

/// <summary>
/// Immutable snapshot of the local installation's identity (Feature 108).
/// </summary>
/// <param name="WalletAddresses">Wallet addresses (Base58) this node operates on behalf of — matched against attestation subject DIDs.</param>
/// <param name="ValidatorPublicKey">Raw bytes of the local validator's signing key; null on nodes that don't run a validator or where the key isn't discoverable.</param>
public sealed record LocalIdentitySnapshot(
    IReadOnlyCollection<string> WalletAddresses,
    byte[]? ValidatorPublicKey)
{
    /// <summary>Empty identity — plain subscriber, no validator.</summary>
    public static LocalIdentitySnapshot Empty { get; } = new(Array.Empty<string>(), null);
}
