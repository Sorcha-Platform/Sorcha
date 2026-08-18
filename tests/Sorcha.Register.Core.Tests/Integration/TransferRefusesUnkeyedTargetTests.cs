// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging;

using Moq;

using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

using Xunit;

namespace Sorcha.Register.Core.Tests.Integration;

/// <summary>
/// Issue #1464 — a Transfer must never promote a roster member that has no public key.
/// </summary>
/// <remarks>
/// <para>
/// Roster authority is matched BY PUBLIC KEY (<c>GovernanceKeyMatcher.Matches</c>, which returns
/// false for an empty key by design). So an Owner carrying an empty key can never sign a governance
/// transaction, its approvals are excluded from every tally, and
/// <c>GovernanceSigningService.SelectSigner</c> prefers the Owner — leaving the register
/// **permanently ungovernable** with no way to route around it.
/// </para>
/// <para>
/// Demonstrated live on n1 (2026-08-17): a governance-added member was transferred ownership, the
/// transfer enacted, and the promoted Owner's next proposal returned HTTP <b>200</b> and never
/// sealed — <c>VAL_PERM_002: none of 1 signature(s) match a roster member</c>. The HTTP layer
/// reported success throughout; it was visible only in the docket and the validator log.
/// </para>
/// <para>
/// This is the fail-closed half of the fix: whatever else changes about how members get keyed,
/// promoting an unkeyed attestation to Owner must be unreachable rather than merely unlikely. It is
/// asserted here, at <c>ApplyOperation</c>, because that is the single point every enactment path
/// funnels through — the two <c>Add</c> sites and the propose-and-enact override all end here.
/// </para>
/// </remarks>
public class TransferRefusesUnkeyedTargetTests
{
    private const string RegisterId = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Fixed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static GovernanceRosterService Service() => new(
        Mock.Of<IReadOnlyRegisterRepository>(), Mock.Of<ILogger<GovernanceRosterService>>());

    private static RegisterAttestation Attestation(string did, RegisterRole role, string publicKey) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = publicKey,
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = Fixed,
    };

    private static string Key(byte seed) => Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray());

    private static RegisterControlRecord RosterWith(params RegisterAttestation[] attestations) => new()
    {
        RegisterId = RegisterId,
        Name = "Consortium",
        CreatedAt = Fixed,
        Attestations = [.. attestations],
    };

    private static GovernanceOperation Transfer(string targetDid) => new()
    {
        OperationType = GovernanceOperationType.Transfer,
        TargetDid = targetDid,
        TargetRole = RegisterRole.Owner,
        ProposedAt = Fixed,
    };

    [Fact]
    public void Transfer_ToAnUnkeyedMember_IsRefused()
    {
        var roster = RosterWith(
            Attestation("did:sorcha:w:owner1", RegisterRole.Owner, Key(1)),
            // A governance-added member: entitled, never keyed.
            Attestation("did:sorcha:w:added1", RegisterRole.Admin, string.Empty));

        var act = () => Service().ApplyOperation(roster, Transfer("did:sorcha:w:added1"));

        act.Should().Throw<InvalidOperationException>(
            "promoting an unkeyed member makes the register permanently ungovernable (#1464)")
            .WithMessage("*did:sorcha:w:added1*");
    }

    [Fact]
    public void Transfer_ToAKeyedMember_StillSucceeds()
    {
        // The guard must not block the legitimate case — this is the operation F189 US4 exists to
        // perform, so an over-broad refusal would be as bad as the bug.
        var roster = RosterWith(
            Attestation("did:sorcha:w:owner1", RegisterRole.Owner, Key(1)),
            Attestation("did:sorcha:w:admin1", RegisterRole.Admin, Key(2)));

        var updated = Service().ApplyOperation(roster, Transfer("did:sorcha:w:admin1"));

        updated.Attestations.Single(a => a.Role == RegisterRole.Owner)
            .Subject.Should().Be("did:sorcha:w:admin1");
        updated.Attestations.Single(a => a.Role == RegisterRole.Owner)
            .PublicKey.Should().Be(Key(2), "the promoted Owner must carry its key across the promotion");
        updated.Attestations.Single(a => a.Subject == "did:sorcha:w:owner1")
            .Role.Should().Be(RegisterRole.Admin, "the outgoing Owner is demoted, not removed");
    }

    [Fact]
    public void Transfer_WhenTheOUTGOINGOwnerIsUnkeyed_StillSucceeds()
    {
        // The guard is about who gains authority, not who loses it. An unkeyed Owner is exactly the
        // broken state #1464 produces, and transferring AWAY from it is the repair — refusing that
        // would make the damage permanent instead of preventing it.
        var roster = RosterWith(
            Attestation("did:sorcha:w:stuck", RegisterRole.Owner, string.Empty),
            Attestation("did:sorcha:w:admin1", RegisterRole.Admin, Key(2)));

        var updated = Service().ApplyOperation(roster, Transfer("did:sorcha:w:admin1"));

        updated.Attestations.Single(a => a.Role == RegisterRole.Owner)
            .Subject.Should().Be("did:sorcha:w:admin1");
    }
}
