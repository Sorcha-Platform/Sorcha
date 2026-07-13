// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Decides whether this node already holds a register authoritatively — in which case it is the
/// <b>source</b> of that register's chain and has nothing to pull from anyone.
/// </summary>
/// <remarks>
/// <para>
/// This is the rule that stops a node pulling from itself. The genesis/seed node subscribes to the
/// system register so it advertises and serves it, but that also put it on the pull path: the
/// replication service looked for source peers, found none (there are none — it IS the source),
/// failed, and left the subscription in <c>Syncing</c> forever. That surfaced to operators as a
/// permanently "Recovering" system register that was in fact perfectly healthy.
/// </para>
/// <para>
/// The discriminator is deliberately the register's own <b>control record</b> (Feature 108: does this
/// node own it, or is its docket-signing key on the validator roster?) and NOT "are there any peers?".
/// A genuine subscriber that momentarily has no reachable peers must keep trying; only the
/// relationship separates "nobody to pull from" from "nothing to pull".
/// </para>
/// </remarks>
internal static class RegisterSourceAuthority
{
    /// <summary>
    /// True when <paramref name="relationship"/> shows this node owns or validates the register.
    /// A null / indeterminate relationship answers <b>false</b>: the node then attempts the pull,
    /// because wrongly pulling costs a round-trip whereas wrongly declining to pull would leave a
    /// real subscriber permanently stale.
    /// </summary>
    internal static bool IsAuthoritativeSource(RegisterLocalRelationship? relationship)
        => relationship is { } r && (r.IsOwner || r.IsValidator);
}
