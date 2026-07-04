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
/// Full governance lifecycle integration test: create register → add admin → remove admin → transfer ownership → verify roster
/// </summary>
public class GovernanceIntegrationTests
{
    private readonly Mock<IRegisterRepository> _repositoryMock;
    private readonly GovernanceRosterService _service;

    private const string RegisterId = "test-register";
    private const string OwnerDid = "did:sorcha:w:owner1";
    private const string Admin1Did = "did:sorcha:w:admin1";
    private const string Admin2Did = "did:sorcha:w:admin2";
    private const string Admin3Did = "did:sorcha:w:admin3";

    public GovernanceIntegrationTests()
    {
        _repositoryMock = new Mock<IRegisterRepository>();
        var logger = new Mock<ILogger<GovernanceRosterService>>();
        _service = new GovernanceRosterService(_repositoryMock.Object, logger.Object);
    }

    private static RegisterAttestation MakeAttestation(string did, RegisterRole role)
    {
        return new RegisterAttestation
        {
            Role = role,
            Subject = did,
            PublicKey = Convert.ToBase64String(new byte[32]),
            Signature = Convert.ToBase64String(new byte[64]),
            Algorithm = SignatureAlgorithm.ED25519,
            GrantedAt = DateTimeOffset.UtcNow
        };
    }

    private static RegisterControlRecord MakeRoster(params (string did, RegisterRole role)[] members)
    {
        return new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = members.Select(m => MakeAttestation(m.did, m.role)).ToList()
        };
    }

    private void SetupControlTransactions(params RegisterControlRecord[] rosterSnapshots)
    {
        var transactions = rosterSnapshots.Select((roster, i) =>
        {
            var payload = new ControlTransactionPayload
            {
                Version = 1,
                Roster = roster
            };
            var payloadBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload);
            var payloadData = Convert.ToBase64String(payloadBytes);

            return new TransactionModel
            {
                TxId = $"tx{i:D4}",
                RegisterId = RegisterId,
                MetaData = new TransactionMetaData
                {
                    RegisterId = RegisterId,
                    TransactionType = TransactionType.Control,
                    BlueprintId = "register-governance-v1"
                },
                Payloads = new[]
                {
                    new PayloadModel { Data = payloadData }
                }
            };
        }).ToList();

        _repositoryMock
            .Setup(r => r.GetTransactionsAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());
        // The roster service now reads control txs via the pushed-down GetTransactionsByTypeAsync.
        _repositoryMock
            .Setup(r => r.GetTransactionsByTypeAsync(RegisterId, TransactionType.Control, It.IsAny<TransactionSort>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions
                .Where(t => t.MetaData is not null && t.MetaData.TransactionType == TransactionType.Control)
                .OrderBy(t => t.DocketNumber ?? 0)
                .ToList());
    }

    [Fact]
    public async Task FullGovernanceCycle_CreateAddRemoveTransfer_ProducesCorrectRoster()
    {
        // Step 1: Genesis — Owner only
        var genesisRoster = MakeRoster((OwnerDid, RegisterRole.Owner));

        // Step 2: Add Admin1
        var addOp1 = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = OwnerDid,
            TargetDid = Admin1Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var validationResult1 = _service.ValidateProposal(
            new AdminRoster { RegisterId = RegisterId, ControlRecord = genesisRoster, ControlTransactionCount = 1 },
            addOp1);
        validationResult1.IsValid.Should().BeTrue("Owner can add an admin");

        var rosterAfterAdd1 = _service.ApplyOperation(genesisRoster, addOp1, MakeAttestation(Admin1Did, RegisterRole.Admin));
        rosterAfterAdd1.Attestations.Should().HaveCount(2);
        rosterAfterAdd1.Attestations.Should().Contain(a => a.Subject == Admin1Did && a.Role == RegisterRole.Admin);

        // Step 3: Add Admin2
        var addOp2 = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = OwnerDid,
            TargetDid = Admin2Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var rosterAfterAdd2 = _service.ApplyOperation(rosterAfterAdd1, addOp2, MakeAttestation(Admin2Did, RegisterRole.Admin));
        rosterAfterAdd2.Attestations.Should().HaveCount(3);

        // Step 4: Add Admin3
        var addOp3 = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = OwnerDid,
            TargetDid = Admin3Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var rosterAfterAdd3 = _service.ApplyOperation(rosterAfterAdd2, addOp3, MakeAttestation(Admin3Did, RegisterRole.Admin));
        rosterAfterAdd3.Attestations.Should().HaveCount(4);

        // Step 5: Remove Admin2 — verify proposal validation
        var removeOp = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = Admin1Did,
            TargetDid = Admin2Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var adminRosterForRemove = new AdminRoster { RegisterId = RegisterId, ControlRecord = rosterAfterAdd3, ControlTransactionCount = 4 };
        var removeValidation = _service.ValidateProposal(adminRosterForRemove, removeOp);
        removeValidation.IsValid.Should().BeTrue("Admin1 can propose removal of Admin2");

        var rosterAfterRemove = _service.ApplyOperation(rosterAfterAdd3, removeOp);
        rosterAfterRemove.Attestations.Should().HaveCount(3);
        rosterAfterRemove.Attestations.Should().NotContain(a => a.Subject == Admin2Did);

        // Step 6: Transfer ownership from Owner to Admin1
        var transferOp = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = OwnerDid,
            TargetDid = Admin1Did,
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        var adminRosterForTransfer = new AdminRoster { RegisterId = RegisterId, ControlRecord = rosterAfterRemove, ControlTransactionCount = 5 };
        var transferValidation = _service.ValidateProposal(adminRosterForTransfer, transferOp);
        transferValidation.IsValid.Should().BeTrue("Owner can propose transfer to Admin1");

        var finalRoster = _service.ApplyOperation(rosterAfterRemove, transferOp);

        // Verify final state
        finalRoster.Attestations.Should().HaveCount(3, "size should be preserved after transfer");
        finalRoster.Attestations.Should().Contain(a => a.Subject == Admin1Did && a.Role == RegisterRole.Owner, "Admin1 should now be Owner");
        finalRoster.Attestations.Should().Contain(a => a.Subject == OwnerDid && a.Role == RegisterRole.Admin, "Original Owner should now be Admin");
        finalRoster.Attestations.Should().Contain(a => a.Subject == Admin3Did && a.Role == RegisterRole.Admin, "Admin3 should remain");
        finalRoster.Attestations.Count(a => a.Role == RegisterRole.Owner).Should().Be(1, "exactly one Owner at all times");

        // Step 7: Verify the roster can be reconstructed from Control TX chain
        SetupControlTransactions(genesisRoster, rosterAfterAdd1, rosterAfterAdd2, rosterAfterAdd3, rosterAfterRemove, finalRoster);
        var reconstructed = await _service.GetCurrentRosterAsync(RegisterId);
        reconstructed.Should().NotBeNull();
        reconstructed!.ControlTransactionCount.Should().Be(6);
        reconstructed.ControlRecord.Attestations.Should().HaveCount(3);
        reconstructed.ControlRecord.Attestations.Should().Contain(a => a.Subject == Admin1Did && a.Role == RegisterRole.Owner);
    }

    [Fact]
    public async Task FullGovernanceCycle_QuorumCheck_EnforcesThresholds()
    {
        // Setup: Owner + 3 Admins (m=4, threshold=3)
        var roster = MakeRoster(
            (OwnerDid, RegisterRole.Owner),
            (Admin1Did, RegisterRole.Admin),
            (Admin2Did, RegisterRole.Admin),
            (Admin3Did, RegisterRole.Admin));

        SetupControlTransactions(roster);

        var addOp = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = Admin1Did,
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        // 2 votes out of 4 → not enough (threshold = 3)
        var twoVotes = new List<ApprovalSignature>
        {
            new() { ApproverDid = Admin1Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = Admin2Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };
        var result2 = await _service.ValidateQuorumAsync(RegisterId, addOp, twoVotes);
        result2.IsQuorumMet.Should().BeFalse();
        result2.VotesRequired.Should().Be(3);
        result2.VotesReceived.Should().Be(2);

        // 3 votes → quorum met
        var threeVotes = new List<ApprovalSignature>
        {
            new() { ApproverDid = Admin1Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = Admin2Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = Admin3Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };
        var result3 = await _service.ValidateQuorumAsync(RegisterId, addOp, threeVotes);
        result3.IsQuorumMet.Should().BeTrue();
        result3.VotesReceived.Should().Be(3);
    }

    [Fact]
    public void FullGovernanceCycle_InvalidOperationsRejected()
    {
        var roster = MakeRoster(
            (OwnerDid, RegisterRole.Owner),
            (Admin1Did, RegisterRole.Admin));
        var adminRoster = new AdminRoster { RegisterId = RegisterId, ControlRecord = roster, ControlTransactionCount = 2 };

        // Cannot add existing member
        var addExisting = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = OwnerDid,
            TargetDid = Admin1Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _service.ValidateProposal(adminRoster, addExisting).IsValid.Should().BeFalse("cannot add existing member");

        // Cannot remove Owner
        var removeOwner = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = Admin1Did,
            TargetDid = OwnerDid,
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _service.ValidateProposal(adminRoster, removeOwner).IsValid.Should().BeFalse("cannot remove Owner via Remove");

        // Non-owner cannot propose transfer
        var transferByAdmin = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = Admin1Did,
            TargetDid = Admin1Did,
            TargetRole = RegisterRole.Owner,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        _service.ValidateProposal(adminRoster, transferByAdmin).IsValid.Should().BeFalse("only Owner can transfer");
    }

    [Fact]
    public async Task FullGovernanceCycle_RemovalExcludesTargetFromQuorum()
    {
        // Owner + Admin1 + Admin2 (m=3, threshold=2)
        // Remove Admin1 → adjusted m=2, adjusted threshold=2
        var roster = MakeRoster(
            (OwnerDid, RegisterRole.Owner),
            (Admin1Did, RegisterRole.Admin),
            (Admin2Did, RegisterRole.Admin));

        SetupControlTransactions(roster);

        var removeOp = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = Admin2Did,
            TargetDid = Admin1Did,
            TargetRole = RegisterRole.Admin,
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        // Admin1 votes for own removal, Admin2 proposes - but Admin1 is excluded from pool
        // So only Owner + Admin2 in pool (m=2, threshold=2)
        var oneVote = new List<ApprovalSignature>
        {
            new() { ApproverDid = Admin2Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };
        var result1 = await _service.ValidateQuorumAsync(RegisterId, removeOp, oneVote);
        result1.IsQuorumMet.Should().BeFalse();
        result1.VotingPool.Should().Be(2, "Admin1 excluded from pool");
        result1.VotesRequired.Should().Be(2);

        // Owner + Admin2 both approve → quorum met
        var twoVotes = new List<ApprovalSignature>
        {
            new() { ApproverDid = Admin2Did, IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = OwnerDid, IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };
        var result2 = await _service.ValidateQuorumAsync(RegisterId, removeOp, twoVotes);
        result2.IsQuorumMet.Should().BeTrue();
    }
}
