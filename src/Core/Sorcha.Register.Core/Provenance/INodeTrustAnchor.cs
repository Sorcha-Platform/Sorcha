// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Core.Provenance;

/// <summary>
/// What this node trusts as the origin of the network it belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Loaded once at startup from the configured or embedded system-register genesis. A node with no
/// genesis — or one whose genesis carries no fingerprint — holds no anchor, which is
/// <c>Unverified</c>, not a failure. See issue #1374, which tracks making the anchor independently
/// configurable.
/// </para>
/// <para>
/// <b>Why this lives in Register.Core (Feature 196).</b> It began as a Register-Service-private
/// interface serving the provenance view. The Validator Service now needs the same anchor to decide
/// whether a transaction claiming to be genesis was actually signed by the network's genesis key —
/// before Feature 196 the validator trusted an unsigned string for that (#1591). Two services
/// needing the network's root of trust must reach the *same* one: a second, independently
/// configured anchor that could disagree with the first is precisely the defect class Feature 196
/// removes. So the interface and its loader moved down into the library both services already
/// depend on, rather than being duplicated.
/// </para>
/// <para>
/// <b>A node that holds no anchor cannot grant the genesis exemption.</b> <see cref="IsKnown"/>
/// false means "I cannot tell", and Feature 196 fails closed on that (FR-007) — the exemption is
/// withheld rather than granted on the transaction's own say-so.
/// </para>
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
