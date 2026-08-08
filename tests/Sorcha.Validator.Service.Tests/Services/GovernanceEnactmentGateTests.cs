// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// A separate enactment is authorised by the approvals sealed against the proposal it names — never
/// by who carried it (Feature 189, T044).
/// </summary>
public sealed class GovernanceEnactmentGateTests
{
    private const string RegisterId = "test-register";
    private const string ProposalId = "proposal-tx";
    private const string OwnerDid = "did:sorcha:w:owner1";
    private const string AdminDid = "did:sorcha:w:admin1";

    private static readonly byte[] OwnerKey = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] AdminKey = Enumerable.Repeat((byte)2, 32).ToArray();

    private readonly Mock<IGovernanceRosterService> _roster = new();
    private readonly Mock<ICryptoModule> _crypto = new();
    private readonly Mock<IReadOnlyRegisterRepository> _repository = new();

    private QuorumResult _quorumVerdict = new()
    {
        IsQuorumMet = false, VotesRequired = 2, VotesReceived = 0, VotingPool = 2
    };

    public GovernanceEnactmentGateTests()
    {
        _roster.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster());
        _roster.Setup(r => r.ValidateProposal(It.IsAny<AdminRoster>(), It.IsAny<GovernanceOperation>()))
            .Returns(GovernanceValidationResult.Success());
        _roster.Setup(r => r.ValidateQuorumAsync(
                It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _quorumVerdict);

        // Reject by default, exactly as the sibling suite does: an approval that is meant to count
        // must opt in, so nothing passes by accident.
        _crypto.Setup(x => x.VerifyAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<byte>(),
                It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CryptoStatus.InvalidSignature);

        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProposalTransaction());
        _repository.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private RightsEnforcementService Service(bool withRepository = true) => new(
        _roster.Object, _crypto.Object, Mock.Of<ILogger<RightsEnforcementService>>(),
        metrics: null, repository: withRepository ? _repository.Object : null);

    private static RegisterAttestation Attestation(string did, RegisterRole role, byte[] key) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = Convert.ToBase64String(key),
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UnixEpoch,
    };

    private static AdminRoster Roster() => new()
    {
        RegisterId = RegisterId,
        ControlRecord = new RegisterControlRecord
        {
            RegisterId = RegisterId,
            Name = "Test",
            CreatedAt = DateTimeOffset.UnixEpoch,
            Attestations =
            [
                Attestation(OwnerDid, RegisterRole.Owner, OwnerKey),
                Attestation(AdminDid, RegisterRole.Admin, AdminKey),
            ]
        },
        ControlTransactionCount = 1,
        LastControlTxId = "genesis-tx",
    };

    private static GovernanceOperation ProposedOperation(
        GovernanceOperationType type = GovernanceOperationType.Add) => new()
    {
        OperationType = type,
        // Raised by the ADMIN, so the Owner override must not apply however it is carried.
        ProposerDid = AdminDid,
        TargetDid = "did:sorcha:w:newadmin",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50),
        RosterSnapshotId = "genesis-tx",
    };

    private static TransactionModel ProposalTransaction()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ControlTransactionPayload
        {
            Version = 1,
            Roster = null,
            Operation = ProposedOperation(),
        });

        return new TransactionModel
        {
            TxId = ProposalId,
            RegisterId = RegisterId,
            Payloads = [new PayloadModel { Data = Convert.ToBase64String(bytes) }],
        };
    }

    /// <summary>An enactment: carries the updated roster and names the proposal it enacts.</summary>
    private static Transaction Enactment(
        byte[] signerKey,
        string? enactsProposalId = ProposalId,
        GovernanceOperationType type = GovernanceOperationType.Add)
    {
        var operation = ProposedOperation(type);
        operation.Status = ProposalStatus.Recorded;

        var payload = new ControlTransactionPayload
        {
            Version = 1,
            Roster = Roster().ControlRecord,
            Operation = operation,
            EnactsProposalId = enactsProposalId,
        };

        return new Transaction
        {
            TransactionId = "enactment-tx",
            RegisterId = RegisterId,
            BlueprintId = RightsEnforcementService.GovernanceBlueprintId,
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload)),
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = signerKey,
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                }
            ]
        };
    }

    /// <summary>
    /// The guard the whole carry/authority separation rests on.
    /// </summary>
    /// <remarks>
    /// The Owner override used to be gated on <c>submitterAttestation.Role</c> — who <b>signed</b> the
    /// transaction. That was sound only while the proposer signed its own enacting transaction. Once
    /// a reaction carries the enactment, an Owner-signed carry would take the override and skip the
    /// quorum check entirely, so a never-approved proposal raised by anyone could be enacted just by
    /// riding it. Who carries a transaction must confer no authority.
    /// </remarks>
    [Fact]
    public async Task AnEnactmentCarriedByTheOwner_WithoutQuorum_IsStillRefused()
    {
        var result = await Service().ValidateGovernanceRightsAsync(Enactment(OwnerKey));

        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_006",
            "the override follows the operation's proposer, never the transaction's signer");
    }

    [Fact]
    public async Task AnEnactmentCarriedByTheOwner_StillConsultsQuorum()
    {
        await Service().ValidateGovernanceRightsAsync(Enactment(OwnerKey));

        _roster.Verify(r => r.ValidateQuorumAsync(
                RegisterId, It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "skipping the call is how the override would silently reappear");
    }

    [Fact]
    public async Task AnEnactment_WithQuorumMetFromSealedApprovals_IsAdmitted()
    {
        _quorumVerdict = new QuorumResult
        {
            IsQuorumMet = true, VotesRequired = 2, VotesReceived = 2, VotingPool = 2
        };

        var result = await Service().ValidateGovernanceRightsAsync(Enactment(OwnerKey));

        result.Errors.Should().NotContain(e => e.Code == "VAL_PERM_005" || e.Code == "VAL_PERM_006");
    }

    [Fact]
    public async Task AnEnactment_WhoseApprovalsCannotBeRead_FailsClosed()
    {
        // No repository: the approvals it claims cannot be read at all. "Unreadable" must never be
        // treated as "none needed".
        var result = await Service(withRepository: false)
            .ValidateGovernanceRightsAsync(Enactment(OwnerKey));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_005");
    }

    [Fact]
    public async Task AnEnactment_NamingAProposalThatDoesNotExist_FailsClosed()
    {
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var result = await Service().ValidateGovernanceRightsAsync(Enactment(OwnerKey));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_PERM_005");
    }

    /// <summary>
    /// A Transfer enactment carries one signature — the carrier's — however many organisations
    /// approved, because the approvals are separate transactions.
    /// </summary>
    [Fact]
    public async Task ATransferEnactment_IsNotRefusedForCarryingOneSignature()
    {
        _quorumVerdict = new QuorumResult
        {
            IsQuorumMet = true, VotesRequired = 2, VotesReceived = 2, VotingPool = 2
        };

        var result = await Service().ValidateGovernanceRightsAsync(
            Enactment(OwnerKey, type: GovernanceOperationType.Transfer));

        result.Errors.Should().NotContain(e => e.Code == "VAL_PERM_008",
            "the signatures that authorise it are on the approval transactions, not on this one");
    }

    /// <summary>
    /// The unchanged path: an Owner-override propose-and-enact names no proposal and keeps its
    /// existing validation exactly (FR-031 / the T086 no-regression gate).
    /// </summary>
    [Fact]
    public async Task AnOwnerOverrideProposeAndEnact_NamingNoProposal_DoesNotConsultSealedApprovals()
    {
        var tx = Enactment(OwnerKey, enactsProposalId: null);

        var result = await Service().ValidateGovernanceRightsAsync(tx);

        result.Errors.Should().NotContain(e => e.Code == "VAL_PERM_005" || e.Code == "VAL_PERM_006",
            "an Owner carrying its own propose-and-enact takes the override, as it always has");
        _repository.Verify(r => r.GetTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
