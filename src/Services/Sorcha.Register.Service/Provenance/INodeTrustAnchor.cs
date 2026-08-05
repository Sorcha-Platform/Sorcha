// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// What this node trusts as the origin of the network it belongs to.
/// </summary>
/// <remarks>
/// Loaded once at startup from the configured or embedded system-register genesis. A node with no
/// genesis — or one whose genesis carries no fingerprint — holds no anchor, which is
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>, not a failure.
/// See issue #1374, which tracks making the anchor independently configurable.
/// </remarks>
public interface INodeTrustAnchor
{
    /// <summary>Whether this node holds a trust anchor at all.</summary>
    bool IsKnown { get; }

    /// <summary>The network label the anchor names (e.g. <c>sorcha-dev</c>). Null when unknown.</summary>
    string? NetworkId { get; }

    /// <summary>
    /// Fingerprint of the genesis signing key the network is anchored on. Null when unknown.
    /// </summary>
    string? GenesisPublicKeyFingerprint { get; }

    /// <summary>
    /// The payload hash of the genesis transaction the anchor describes — what a node's own stored
    /// system-register genesis must match to be the genesis this anchor covers. Null when unknown.
    /// </summary>
    string? GenesisPayloadHash { get; }
}
