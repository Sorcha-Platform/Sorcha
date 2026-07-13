// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models.LocalRelationship;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Replication;

/// <summary>
/// A node must never try to pull a register it is itself the source of.
/// <para>
/// Found live on n1: the genesis node subscribes to the system register (so it advertises and serves
/// it), which also put it on the pull path. Replication looked for source peers, found none — there
/// are none, it IS the source — failed, and left the subscription in <c>Syncing</c> forever. Register
/// Service mapped that to "Recovery", so a perfectly healthy system register (height 5, sealing its
/// own dockets) reported itself as permanently recovering.
/// </para>
/// </summary>
public class RegisterSourceAuthorityTests
{
    private static RegisterLocalRelationship Relationship(RegisterRoleSet roles) =>
        new("reg-1", roles, ControlRecordVersion: 1, DerivedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Validator_IsAuthoritativeSource_SoItDoesNotPull()
    {
        // The n1 case: this node's docket-signing key is the sole entry on the register's roster and
        // it seals every docket. It holds the authoritative chain.
        RegisterSourceAuthority.IsAuthoritativeSource(Relationship(RegisterRoleSet.Validator))
            .Should().BeTrue();
    }

    [Fact]
    public void Owner_IsAuthoritativeSource_SoItDoesNotPull()
    {
        RegisterSourceAuthority.IsAuthoritativeSource(Relationship(RegisterRoleSet.Owner))
            .Should().BeTrue();
    }

    [Fact]
    public void Subscriber_IsNotAuthoritative_AndMustKeepPulling()
    {
        // The load-bearing negative: a real subscriber that momentarily has no reachable peers must
        // keep trying. "No peers" must never be mistaken for "nothing to pull".
        RegisterSourceAuthority.IsAuthoritativeSource(Relationship(RegisterRoleSet.None))
            .Should().BeFalse();
    }

    [Fact]
    public void UnknownRelationship_FailsSafeToPulling()
    {
        // Register Service unreachable / relationship indeterminate. Wrongly pulling costs a
        // round-trip; wrongly declining to pull would strand a subscriber forever.
        RegisterSourceAuthority.IsAuthoritativeSource(null).Should().BeFalse();
    }

    [Fact]
    public void AuditorOrDesigner_AloneIsNotASource()
    {
        // Read-only roles on the control record confer no chain authority — such a node still pulls.
        RegisterSourceAuthority.IsAuthoritativeSource(
            Relationship(RegisterRoleSet.Auditor | RegisterRoleSet.Designer)).Should().BeFalse();
    }
}
