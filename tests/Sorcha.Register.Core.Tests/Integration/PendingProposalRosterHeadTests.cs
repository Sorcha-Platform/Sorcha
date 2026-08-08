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
/// A pending governance proposal must not move the roster head (Feature 189).
/// </summary>
/// <remarks>
/// <para>
/// This is the property the whole propose/approve/enact split rests on, so it is asserted rather
/// than assumed. FR-011b invalidates a proposal whose <c>RosterSnapshotId</c> no longer matches
/// <c>LastControlTxId</c>. A proposal that wrote a roster snapshot would become the newest
/// roster-bearing control transaction and therefore move that value — invalidating <b>itself</b> the
/// instant it sealed, so no approval could ever attach to it.
/// </para>
/// <para>
/// That was not hypothetical: it is what the pre-split propose-and-enact transaction did, confirmed
/// on the n1 fixture register, where the sealed proposal carried its own roster with the operation
/// already applied.
/// </para>
/// </remarks>
public class PendingProposalRosterHeadTests
{
    private const string RegisterId = "test-register";

    private static RegisterControlRecord MakeRoster(params (string did, RegisterRole role)[] members) => new()
    {
        RegisterId = RegisterId,
        Name = "Test",
        CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Attestations = members.Select(m => new RegisterAttestation
        {
            Role = m.role,
            Subject = m.did,
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        }).ToList()
    };

    private static GovernanceOperation PendingAdd() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = "did:sorcha:w:admin1",
        TargetDid = "did:sorcha:w:newadmin",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero)
    };

    /// <summary>Builds a control chain in seal order; a null roster means "changes no roster".</summary>
    private static GovernanceRosterService CreateService(
        params (string txId, RegisterControlRecord? roster, GovernanceOperation? operation)[] chain)
    {
        var transactions = chain.Select((entry, i) =>
        {
            var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new ControlTransactionPayload
                {
                    Version = 1,
                    Roster = entry.roster,
                    Operation = entry.operation
                });

            return new TransactionModel
            {
                TxId = entry.txId,
                RegisterId = RegisterId,
                DocketNumber = (ulong)i,
                MetaData = new TransactionMetaData
                {
                    RegisterId = RegisterId,
                    TransactionType = TransactionType.Control,
                    BlueprintId = GovernanceBlueprint.BlueprintId
                },
                Payloads = [new PayloadModel { Data = Convert.ToBase64String(payloadBytes) }]
            };
        }).ToList();

        var repository = new Mock<IRegisterRepository>();
        repository
            .Setup(r => r.GetTransactionsAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());
        repository
            .Setup(r => r.GetTransactionsByTypeAsync(
                RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.OrderBy(t => t.DocketNumber ?? 0).ToList());

        return new GovernanceRosterService(
            repository.Object, Mock.Of<ILogger<GovernanceRosterService>>());
    }

    [Fact]
    public async Task APendingProposal_DoesNotBecomeTheRosterHead()
    {
        var service = CreateService(
            ("genesis", MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner)), null),
            ("proposal", null, PendingAdd()));

        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster.Should().NotBeNull();
        roster!.LastControlTxId.Should().Be("genesis",
            "a proposal that changes no roster must leave the head on the last real roster commit — "
            + "otherwise it invalidates itself under FR-011b the moment it seals");
    }

    [Fact]
    public async Task APendingProposal_LeavesTheRosterItselfUntouched()
    {
        var service = CreateService(
            ("genesis", MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner)), null),
            ("proposal", null, PendingAdd()));

        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster!.ControlRecord.Attestations.Should().ContainSingle(
            "the proposal proposes an Add; nothing is added until it is enacted");
    }

    [Fact]
    public async Task SeveralOpenProposals_DoNotInvalidateEachOther()
    {
        // Under the pre-split shape each raised proposal moved the head, so raising a second one
        // killed the first. Concurrent proposals are the ordinary case for a consortium.
        var service = CreateService(
            ("genesis", MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner)), null),
            ("proposal-a", null, PendingAdd()),
            ("proposal-b", null, PendingAdd()));

        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster!.LastControlTxId.Should().Be("genesis");
    }

    [Fact]
    public async Task AnEnactment_DoesMoveTheRosterHead()
    {
        // The contrast that keeps the three tests above honest: if reconstruction simply never
        // advanced, they would all pass while proving nothing.
        var service = CreateService(
            ("genesis", MakeRoster(("did:sorcha:w:owner1", RegisterRole.Owner)), null),
            ("proposal", null, PendingAdd()),
            ("enactment", MakeRoster(
                ("did:sorcha:w:owner1", RegisterRole.Owner),
                ("did:sorcha:w:newadmin", RegisterRole.Admin)), PendingAdd()));

        var roster = await service.GetCurrentRosterAsync(RegisterId);

        roster!.LastControlTxId.Should().Be("enactment");
        roster.ControlRecord.Attestations.Should().HaveCount(2);
    }
}
