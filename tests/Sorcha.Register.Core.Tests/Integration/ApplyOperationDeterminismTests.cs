// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Core.Tests.Integration;

/// <summary>
/// Two nodes enacting the same proposal must produce byte-identical payloads (Feature 189).
/// </summary>
/// <remarks>
/// <para>
/// The enactment transaction id is derived deterministically so a concurrent submission from a second
/// node dedupes rather than forks. That only holds if the <i>payload</i> is identical too: same id
/// with a different payload hash is a conflicting duplicate, and it reads as a fork rather than as the
/// idempotent resubmission it is.
/// </para>
/// <para>
/// The Transfer arms of <c>ApplyOperation</c> stamped <c>DateTimeOffset.UtcNow</c>, so they were not.
/// There is no symptom until two nodes enact concurrently — which is exactly why this is asserted
/// rather than reasoned about.
/// </para>
/// </remarks>
public class ApplyOperationDeterminismTests
{
    private const string RegisterId = "test-register";

    private static GovernanceRosterService Service() => new(
        Mock.Of<IReadOnlyRegisterRepository>(), Mock.Of<ILogger<GovernanceRosterService>>());

    private static RegisterAttestation Attestation(string did, RegisterRole role, byte seed) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()),
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
    };

    private static RegisterControlRecord Roster() => new()
    {
        RegisterId = RegisterId,
        Name = "Test",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Attestations =
        [
            Attestation("did:sorcha:w:owner1", RegisterRole.Owner, 1),
            Attestation("did:sorcha:w:admin1", RegisterRole.Admin, 2),
        ]
    };

    private static GovernanceOperation Transfer() => new()
    {
        OperationType = GovernanceOperationType.Transfer,
        ProposerDid = "did:sorcha:w:owner1",
        TargetDid = "did:sorcha:w:admin1",
        TargetRole = RegisterRole.Owner,
        Status = ProposalStatus.Pending,
        ProposedAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 8, 12, 0, 0, TimeSpan.Zero),
    };

    private static string Canonical(RegisterControlRecord record) =>
        System.Text.Json.JsonSerializer.Serialize(record);

    [Fact]
    public async Task TwoIndependentEnactments_OfTheSameTransfer_AreByteIdentical()
    {
        var first = Canonical(Service().ApplyOperation(Roster(), Transfer()));

        // A gap wide enough that any clock read lands on a different value.
        await Task.Delay(1100);

        var second = Canonical(Service().ApplyOperation(Roster(), Transfer()));

        second.Should().Be(first,
            "the same proposal enacted on two nodes must produce the same payload bytes, or the "
            + "deterministic transaction id collides with a different payload hash");
    }

    [Fact]
    public void ATransfersTimestamps_ComeFromTheProposal_NotTheClock()
    {
        var operation = Transfer();

        var updated = Service().ApplyOperation(Roster(), operation);

        updated.Attestations.Should().OnlyContain(a => a.GrantedAt == operation.ProposedAt,
            "every timestamp in an enactment must derive from sealed content");
    }

    [Fact]
    public async Task TwoIndependentEnactments_OfTheSameAdd_AreByteIdentical()
    {
        // The Add arm takes its attestation from the caller, so this asserts the caller-supplied
        // shape survives unchanged rather than being re-stamped inside.
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero),
        };

        RegisterAttestation NewMember() => new()
        {
            Subject = operation.TargetDid,
            Role = operation.TargetRole,
            PublicKey = string.Empty,
            Signature = string.Empty,
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = operation.ProposedAt,
        };

        var first = Canonical(Service().ApplyOperation(Roster(), operation, NewMember()));
        await Task.Delay(1100);
        var second = Canonical(Service().ApplyOperation(Roster(), operation, NewMember()));

        second.Should().Be(first);
    }
}
