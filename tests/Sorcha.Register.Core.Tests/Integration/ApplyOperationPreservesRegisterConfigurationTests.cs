// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
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
/// A governance operation changes the roster's <b>membership</b>. It must change nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyOperation</c> rebuilds the control record field by field, and the rebuild listed six of
/// the ten properties on <see cref="RegisterControlRecord"/>. The four it omitted —
/// <c>CryptoPolicy</c>, <c>RegisterPolicy</c>, <c>RoutingAttestation</c> and <c>Validators</c> —
/// were therefore dropped by every enacted Add, Remove and Transfer, and
/// <c>GetCurrentRosterAsync</c> takes the newest roster-bearing payload <i>wholesale</i>, so they
/// did not come back.
/// </para>
/// <para>
/// Nothing failed. The register kept sealing, because both readers of the validator roster
/// (<c>RegisterLocalRelationshipService</c> and the peer <c>ValidatorKeyCache</c>) resolve it from
/// the <b>genesis</b> docket rather than the current one. What moved was the governance rule itself:
/// the quorum formula is read from <c>RegisterPolicy.Governance.QuorumFormula</c>, so a consortium
/// register configured for <see cref="QuorumFormula.Unanimous"/> silently fell back to the
/// <see cref="QuorumFormula.StrictMajority"/> default the moment its first governance change
/// enacted — three-of-three quietly becoming two-of-three. Observed on live data (n1 register
/// <c>a17208cf…</c>, the Feature 189 T048 gate's own register).
/// </para>
/// <para>
/// Asserted by reflection rather than against a hand-written field list, because a hand-written list
/// rots in the same direction as the bug: the next property added to
/// <see cref="RegisterControlRecord"/> would be dropped exactly like these four, and a list-based
/// test would stay green while it happened.
/// </para>
/// </remarks>
public class ApplyOperationPreservesRegisterConfigurationTests
{
    private const string RegisterId = "0123456789abcdef0123456789abcdef";

    /// <summary>The one property a governance operation is allowed to change.</summary>
    private const string MembershipProperty = nameof(RegisterControlRecord.Attestations);

    private static GovernanceRosterService Service() => new(
        Mock.Of<IReadOnlyRegisterRepository>(), Mock.Of<ILogger<GovernanceRosterService>>());

    private static readonly DateTimeOffset Fixed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RegisterAttestation Attestation(string did, RegisterRole role, byte seed) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()),
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = Fixed,
    };

    /// <summary>
    /// A control record with EVERY optional section populated, and populated with non-default values
    /// — a policy left at its default is indistinguishable from one that was dropped and re-defaulted
    /// at read time, which is precisely the failure being guarded against.
    /// </summary>
    private static RegisterControlRecord FullyConfiguredRoster() => new()
    {
        RegisterId = RegisterId,
        Name = "Consortium",
        Description = "Every optional section populated",
        CreatedAt = Fixed,
        Attestations =
        [
            Attestation("did:sorcha:w:owner1", RegisterRole.Owner, 1),
            Attestation("did:sorcha:w:admin1", RegisterRole.Admin, 2),
        ],
        CryptoPolicy = new CryptoPolicy
        {
            Version = 3,
            AcceptedSignatureAlgorithms = ["ED25519"],
            EnforcementMode = CryptoPolicyEnforcementMode.Strict,
            DevMode = false,
        },
        RegisterPolicy = new RegisterPolicy
        {
            Version = 2,
            Governance = new PolicyGovernanceConfig
            {
                // The load-bearing value: this is what ValidateQuorumAsync reads.
                QuorumFormula = QuorumFormula.Unanimous,
                ProposalTtlDays = 14,
                OwnerCanBypassQuorum = false,
            },
        },
        RoutingAttestation = AttestationKind.ValidatorReEvaluated,
        Validators = new ValidatorRoster
        {
            Version = 4,
            RequiredSignatures = 2,
            Validators =
            [
                new ValidatorRosterEntry
                {
                    ValidatorId = "ws11qvalidator",
                    PublicKey = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()),
                    Algorithm = SignatureAlgorithm.ED25519,
                    Status = ValidatorKeyStatus.Active,
                    AuthorizedAt = Fixed,
                },
            ],
        },
        Metadata = new Dictionary<string, string> { ["category"] = "consortium" },
    };

    private static GovernanceOperation Operation(GovernanceOperationType type, string targetDid) => new()
    {
        OperationType = type,
        ProposerDid = "did:sorcha:w:owner1",
        TargetDid = targetDid,
        TargetRole = RegisterRole.Admin,
        ProposedAt = Fixed,
    };

    public static TheoryData<GovernanceOperationType, string> MembershipOperations() => new()
    {
        { GovernanceOperationType.Add, "did:sorcha:w:newcomer" },
        { GovernanceOperationType.Remove, "did:sorcha:w:admin1" },
        { GovernanceOperationType.Transfer, "did:sorcha:w:admin1" },
    };

    [Theory]
    [MemberData(nameof(MembershipOperations))]
    public void ApplyOperation_CarriesEveryNonMembershipPropertyForward(
        GovernanceOperationType operationType, string targetDid)
    {
        var before = FullyConfiguredRoster();
        var operation = Operation(operationType, targetDid);

        var newAttestation = operationType == GovernanceOperationType.Add
            ? Attestation(targetDid, RegisterRole.Admin, 3)
            : null;

        var after = Service().ApplyOperation(before, operation, newAttestation);

        // Reflection, not a field list: the next property added to RegisterControlRecord is caught
        // by this test on the day it is added, not on the day it silently goes missing in production.
        var carried = typeof(RegisterControlRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.Name != MembershipProperty)
            .ToList();

        carried.Should().NotBeEmpty("the record has properties other than its membership list");

        using var _ = new FluentAssertions.Execution.AssertionScope();
        foreach (var property in carried)
        {
            property.GetValue(after).Should().BeEquivalentTo(
                property.GetValue(before),
                $"a {operationType} changes roster membership and must leave {property.Name} untouched");
        }
    }

    /// <summary>
    /// The consequence, stated in the terms an operator would notice: the rule the register is
    /// governed by must survive being governed.
    /// </summary>
    [Fact]
    public void ApplyOperation_LeavesTheQuorumFormulaTheRegisterIsJudgedBy()
    {
        var before = FullyConfiguredRoster();

        var after = Service().ApplyOperation(
            before,
            Operation(GovernanceOperationType.Add, "did:sorcha:w:newcomer"),
            Attestation("did:sorcha:w:newcomer", RegisterRole.Admin, 3));

        after.RegisterPolicy?.Governance.QuorumFormula.Should().Be(
            QuorumFormula.Unanimous,
            "a register configured for unanimity must not fall back to the StrictMajority default "
            + "just because a member was added — that turns three-of-three into two-of-three with "
            + "nothing reported");

        // The threshold is the observable the validator actually acts on.
        after.GetQuorumThreshold(
                formula: after.RegisterPolicy?.Governance.QuorumFormula ?? QuorumFormula.StrictMajority)
            .Should().Be(3, "unanimity over three voting members requires all three");
    }

    /// <summary>
    /// A validator-roster operation must actually produce a validator roster.
    /// </summary>
    /// <remarks>
    /// <c>GovernanceEnactmentService.ProjectRoster</c> applies the validator-roster change and then
    /// passes the result through <c>ApplyOperation</c>, so dropping <c>Validators</c> here discarded
    /// the very change the operation existed to make: AddValidator / RemoveValidator /
    /// RotateValidatorKey enacted "successfully" and wrote a roster with no validators at all.
    /// </remarks>
    [Fact]
    public void ApplyOperation_DoesNotDiscardAValidatorRosterChangeMadeByItsCaller()
    {
        var before = FullyConfiguredRoster();

        // Stands in for what ApplyValidatorRosterChange hands over: the same record, validators
        // already updated. ApplyOperation has no case for AddValidator, so its only job here is not
        // to throw the work away.
        var after = Service().ApplyOperation(
            before, Operation(GovernanceOperationType.AddValidator, "ws11qvalidator"), null);

        after.Validators.Should().NotBeNull(
            "an enacted validator-roster operation that writes no validators leaves the register "
            + "unable to name who may seal its dockets");
        after.Validators!.Validators.Should().HaveCount(1);
        after.Validators.RequiredSignatures.Should().Be(2);
    }
}
