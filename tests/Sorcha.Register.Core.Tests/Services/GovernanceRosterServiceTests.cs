// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Core.Tests.Services;

public class GovernanceRosterServiceTests
{
    private readonly Mock<IRegisterRepository> _repositoryMock;
    private readonly GovernanceRosterService _service;
    private const string TestRegisterId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";

    public GovernanceRosterServiceTests()
    {
        _repositoryMock = new Mock<IRegisterRepository>();
        var logger = new Mock<ILogger<GovernanceRosterService>>();
        _service = new GovernanceRosterService(_repositoryMock.Object, logger.Object);

        // Default: return empty transactions
        _repositoryMock
            .Setup(r => r.GetTransactionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>().AsQueryable());
    }

    // --- GetCurrentRosterAsync ---

    [Fact]
    public async Task GetCurrentRosterAsync_NoControlTransactions_ReturnsNull()
    {
        var result = await _service.GetCurrentRosterAsync(TestRegisterId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentRosterAsync_SingleGenesisControlTx_ReturnsSingleOwner()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var result = await _service.GetCurrentRosterAsync(TestRegisterId);

        result.Should().NotBeNull();
        result!.ControlRecord.Attestations.Should().HaveCount(1);
        result.ControlRecord.Attestations[0].Role.Should().Be(RegisterRole.Owner);
        result.ControlRecord.Attestations[0].Subject.Should().Be("did:sorcha:w:owner1");
        result.ControlTransactionCount.Should().Be(1);
        result.LastControlTxId.Should().Be("tx1");
    }

    [Fact]
    public async Task GetCurrentRosterAsync_MultipleControlTxs_ReturnsLatestRoster()
    {
        var roster1 = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var roster2 = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));

        SetupControlTransactions(
            CreateControlTransaction("tx1", roster1),
            CreateControlTransaction("tx2", roster2));

        var result = await _service.GetCurrentRosterAsync(TestRegisterId);

        result.Should().NotBeNull();
        result!.ControlRecord.Attestations.Should().HaveCount(2);
        result.ControlTransactionCount.Should().Be(2);
        result.LastControlTxId.Should().Be("tx2");
    }

    [Fact]
    public async Task GetCurrentRosterAsync_IgnoresActionTransactions()
    {
        var roster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var controlTx = CreateControlTransaction("tx1", roster);
        var actionTx = new TransactionModel
        {
            TxId = "tx-action",
            MetaData = new TransactionMetaData
            {
                RegisterId = TestRegisterId,
                TransactionType = TransactionType.Action
            },
            Payloads = []
        };

        _repositoryMock
            .Setup(r => r.GetTransactionsAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { controlTx, actionTx }.AsQueryable());

        var result = await _service.GetCurrentRosterAsync(TestRegisterId);

        result.Should().NotBeNull();
        result!.ControlTransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentRosterAsync_LatestControlTxHasUnrelatedPayload_FallsBackToLatestWithRoster()
    {
        // Regression: TransactionType.Control is broader than "governance roster snapshot".
        // The blueprint workflow path can write a Control-typed transaction whose payload is
        // an action-submission record, NOT a ControlTransactionPayload. The previous
        // implementation picked the LAST control tx unconditionally, deserialised its
        // unrelated payload into a default-empty ControlTransactionPayload (no attestations,
        // no validator roster), and silently returned `members: []` — which made the F142
        // PublishGate refuse every publish with "lacks publish-governance role".
        //
        // The fix walks newest → oldest and picks the latest control tx that actually carries
        // a populated roster. Older real-genesis tx therefore wins over the noise.
        var genesisRoster = CreateRoster(("did:sorcha:w:owner1", RegisterRole.Owner));
        var genesisTx = CreateControlTransaction("tx-genesis", genesisRoster);

        var noiseTx = new TransactionModel
        {
            TxId = "tx-noise",
            MetaData = new TransactionMetaData
            {
                RegisterId = TestRegisterId,
                TransactionType = TransactionType.Control,
            },
            Payloads = [new PayloadModel
            {
                // An action-submission payload: arbitrary JSON, not ControlTransactionPayload
                // shaped, so .Roster on deserialise is null and the policy ought to skip it.
                Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                    "{\"action\":1,\"applicantName\":\"Alex MacLeod\"}")),
            }],
        };

        _repositoryMock
            .Setup(r => r.GetTransactionsAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { genesisTx, noiseTx }.AsQueryable());

        var result = await _service.GetCurrentRosterAsync(TestRegisterId);

        result.Should().NotBeNull();
        result!.ControlRecord.Attestations.Should().HaveCount(1);
        result.ControlRecord.Attestations[0].Subject.Should().Be("did:sorcha:w:owner1");
        result.LastControlTxId.Should().Be("tx-genesis");
        // Both transactions are still counted (the noise is a real control tx, the count is
        // informational); the picked one is the latest with a real roster.
        result.ControlTransactionCount.Should().Be(2);
    }

    // --- ValidateQuorumAsync ---

    [Fact]
    public async Task ValidateQuorumAsync_OwnerOverride_ReturnsQuorumMet()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };

        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, []);

        result.IsQuorumMet.Should().BeTrue();
        result.IsOwnerOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateQuorumAsync_TwoVoters_BothRequired()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:admin1", // Not owner — no override
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };

        // Only one approval
        var approvals = new List<ApprovalSignature>
        {
            new() { ApproverDid = "did:sorcha:w:admin1", IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };

        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, approvals);

        result.IsQuorumMet.Should().BeFalse();
        result.VotesRequired.Should().Be(2); // floor(2/2)+1 = 2
        result.VotesReceived.Should().Be(1);
        result.VotingPool.Should().Be(2);
    }

    [Fact]
    public async Task ValidateQuorumAsync_RemovalExcludesTarget()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Remove,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:admin2",
            TargetRole = RegisterRole.Admin
        };

        // Owner + admin1 approve (admin2 excluded)
        var approvals = new List<ApprovalSignature>
        {
            new() { ApproverDid = "did:sorcha:w:owner1", IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = "did:sorcha:w:admin1", IsApproval = true, VotedAt = DateTimeOffset.UtcNow }
        };

        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, approvals);

        result.IsQuorumMet.Should().BeTrue();
        result.VotingPool.Should().Be(2); // 3 minus excluded admin2
        result.VotesReceived.Should().Be(2);
    }

    [Fact]
    public async Task ValidateQuorumAsync_NoRegister_ReturnsNotMet()
    {
        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:someone",
            TargetDid = "did:sorcha:w:newadmin"
        };

        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, []);

        result.IsQuorumMet.Should().BeFalse();
        result.VotingPool.Should().Be(0);
    }

    [Fact]
    public async Task ValidateQuorumAsync_TransferByOwner_NoOwnerOverride()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Transfer,
            ProposerDid = "did:sorcha:w:owner1",
            TargetDid = "did:sorcha:w:admin1",
            TargetRole = RegisterRole.Owner
        };

        // Transfer doesn't get Owner override — requires normal quorum
        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, []);

        result.IsOwnerOverride.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateQuorumAsync_RejectionsNotCounted()
    {
        var roster = CreateRoster(
            ("did:sorcha:w:owner1", RegisterRole.Owner),
            ("did:sorcha:w:admin1", RegisterRole.Admin),
            ("did:sorcha:w:admin2", RegisterRole.Admin));
        SetupControlTransactions(CreateControlTransaction("tx1", roster));

        var operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            ProposerDid = "did:sorcha:w:admin1",
            TargetDid = "did:sorcha:w:newadmin",
            TargetRole = RegisterRole.Admin
        };

        var approvals = new List<ApprovalSignature>
        {
            new() { ApproverDid = "did:sorcha:w:owner1", IsApproval = true, VotedAt = DateTimeOffset.UtcNow },
            new() { ApproverDid = "did:sorcha:w:admin2", IsApproval = false, VotedAt = DateTimeOffset.UtcNow } // Rejection
        };

        var result = await _service.ValidateQuorumAsync(TestRegisterId, operation, approvals);

        result.VotesReceived.Should().Be(1); // Only the approval counts
    }

    // --- Helpers ---

    private RegisterControlRecord CreateRoster(params (string did, RegisterRole role)[] members)
    {
        return new RegisterControlRecord
        {
            RegisterId = TestRegisterId,
            Name = "Test Register",
            CreatedAt = DateTimeOffset.UtcNow,
            Attestations = members.Select(m => new RegisterAttestation
            {
                Role = m.role,
                Subject = m.did,
                PublicKey = Convert.ToBase64String(new byte[32]),
                Signature = Convert.ToBase64String(new byte[64]),
                Algorithm = SignatureAlgorithm.ED25519,
                GrantedAt = DateTimeOffset.UtcNow
            }).ToList()
        };
    }

    private TransactionModel CreateControlTransaction(string txId, RegisterControlRecord roster,
        GovernanceOperation? operation = null)
    {
        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = roster,
            Operation = operation
        };

        var payloadBytes = JsonSerializer.Serialize(payload);
        var base64Payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadBytes));

        return new TransactionModel
        {
            TxId = txId,
            MetaData = new TransactionMetaData
            {
                RegisterId = TestRegisterId,
                TransactionType = TransactionType.Control
            },
            Payloads = [new PayloadModel { Data = base64Payload }]
        };
    }

    private void SetupControlTransactions(params TransactionModel[] transactions)
    {
        _repositoryMock
            .Setup(r => r.GetTransactionsAsync(TestRegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions.AsQueryable());
    }
}
