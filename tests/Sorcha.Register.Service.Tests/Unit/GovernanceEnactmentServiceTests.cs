// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Raising the enactment once a proposal's approvals reach quorum (T044b).
/// </summary>
public sealed class GovernanceEnactmentServiceTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ProposalId = "proposal-tx";
    private const string RosterHeadTxId = "genesis-tx";
    private const string OwnerDid = "did:sorcha:w:ws11qowner";
    private const string AdminDid = "did:sorcha:w:ws11qadmin";

    private readonly Mock<IGovernanceProposalReader> _reader = new();
    private readonly Mock<IGovernanceRosterService> _rosterService = new();
    private readonly Mock<IReadOnlyRegisterRepository> _repository = new();
    private readonly Mock<IGovernanceSigningService> _signing = new();
    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly GovernanceEnactmentService _sut;

    private TransactionSubmission? _captured;

    private QuorumResult _quorum = new()
    {
        IsQuorumMet = false, VotesRequired = 2, VotesReceived = 0, VotingPool = 2
    };

    public GovernanceEnactmentServiceTests()
    {
        _reader.Setup(r => r.ReadAsync(RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new GovernanceProposalRead(
                ProposalReadOutcome.Found, PendingOperation(), null));

        _rosterService.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Roster);
        _rosterService.Setup(r => r.ValidateQuorumAsync(
                It.IsAny<string>(), It.IsAny<GovernanceOperation>(),
                It.IsAny<List<ApprovalSignature>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _quorum);
        _rosterService.Setup(r => r.ApplyOperation(
                It.IsAny<RegisterControlRecord>(), It.IsAny<GovernanceOperation>(),
                It.IsAny<RegisterAttestation?>()))
            .Returns((RegisterControlRecord c, GovernanceOperation _, RegisterAttestation? a) =>
            {
                var updated = Roster().ControlRecord;
                if (a is not null) updated.Attestations.Add(a);
                return updated;
            });

        // No enactment on the register yet.
        _repository.Setup(r => r.GetTransactionAsync(
                RegisterId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);
        _repository.Setup(r => r.GetTransactionsByPrevTxIdAsync(
                RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _signing.Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceSignResult
            {
                Signature = [1, 2, 3, 4],
                PublicKey = [5, 6, 7, 8],
                Algorithm = "ED25519",
                WalletAddress = "ws11qowner",
                Subject = OwnerDid,
            });

        _validator.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionSubmission s, CancellationToken _) => _captured = s)
            .ReturnsAsync(() => new TransactionSubmissionResult { Success = true });

        _sut = new GovernanceEnactmentService(
            _reader.Object, _rosterService.Object, _repository.Object,
            _signing.Object, _validator.Object,
            Mock.Of<ILogger<GovernanceEnactmentService>>());
    }

    private static RegisterAttestation Attestation(string did, RegisterRole role, byte seed) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray()),
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
                Attestation(OwnerDid, RegisterRole.Owner, 1),
                Attestation(AdminDid, RegisterRole.Admin, 2),
            ]
        },
        ControlTransactionCount = 1,
        LastControlTxId = RosterHeadTxId,
    };

    private static GovernanceOperation PendingOperation() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = AdminDid,
        TargetDid = "did:sorcha:w:ws11qnewadmin",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddYears(50),
        RosterSnapshotId = RosterHeadTxId,
    };

    private void QuorumMet() => _quorum = new QuorumResult
    {
        IsQuorumMet = true, VotesRequired = 2, VotesReceived = 2, VotingPool = 2
    };

    [Fact]
    public async Task WithoutQuorum_NothingIsSubmitted()
    {
        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.QuorumNotMet);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithQuorum_TheEnactmentIsSubmittedThroughTheValidator()
    {
        QuorumMet();

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.Submitted);
        _captured.Should().NotBeNull();
        _captured!.BlueprintId.Should().Be(GovernanceBlueprint.BlueprintId);
        _captured.ActionId.Should().Be(GovernanceBlueprint.RecordControlTransactionActionId.ToString());
    }

    [Fact]
    public async Task TheEnactmentCarriesARosterAndNamesItsProposal()
    {
        // Both are load-bearing: the roster is what makes it the change, and the proposal reference is
        // where the Validator finds the approvals that authorise it.
        QuorumMet();

        await _sut.TryEnactAsync(RegisterId, ProposalId);

        var payload = JsonSerializer.Deserialize<ControlTransactionPayload>(
            _captured!.Payload.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        payload.Roster.Should().NotBeNull("an enactment is the roster mutation");
        payload.EnactsProposalId.Should().Be(ProposalId);
        payload.Operation!.Status.Should().Be(ProposalStatus.Recorded);
    }

    [Fact]
    public async Task TheEnactmentChainsOffTheRosterHead()
    {
        QuorumMet();

        await _sut.TryEnactAsync(RegisterId, ProposalId);

        _captured!.PreviousTransactionId.Should().Be(RosterHeadTxId);
    }

    [Fact]
    public async Task TheEnactmentIdIsDeterministic_SoConcurrentNodesDedupeRatherThanFork()
    {
        QuorumMet();

        var first = await _sut.TryEnactAsync(RegisterId, ProposalId);
        var second = await _sut.TryEnactAsync(RegisterId, ProposalId);

        second.TxId.Should().Be(first.TxId);
        first.TxId.Should().Be(
            GovernanceEnactmentService.ComputeEnactmentTransactionId(RegisterId, ProposalId));
    }

    /// <summary>
    /// The bytes two nodes produce must match, or the deterministic id collides with a different
    /// payload hash — which reads as a fork, not as the resubmission it is.
    /// </summary>
    [Fact]
    public async Task TwoIndependentlyBuiltEnactments_AreByteIdentical()
    {
        QuorumMet();

        await _sut.TryEnactAsync(RegisterId, ProposalId);
        var first = _captured!.Payload.GetRawText();
        var firstHash = _captured.PayloadHash;

        await Task.Delay(1100);   // wide enough that any clock read would differ

        await _sut.TryEnactAsync(RegisterId, ProposalId);

        _captured!.Payload.GetRawText().Should().Be(first);
        _captured.PayloadHash.Should().Be(firstHash);
    }

    [Fact]
    public async Task AnAlreadyEnactedProposal_IsNotEnactedAgain()
    {
        QuorumMet();
        var enactmentId = GovernanceEnactmentService.ComputeEnactmentTransactionId(RegisterId, ProposalId);
        _repository.Setup(r => r.GetTransactionAsync(RegisterId, enactmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel { TxId = enactmentId, RegisterId = RegisterId });

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.AlreadyEnacted);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ANodeHoldingNoGovernanceKey_DoesNotCarryTheEnactment()
    {
        // Not an error. A subscriber node simply is not the one to raise it.
        QuorumMet();
        _signing.Setup(s => s.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no roster member with a governance role"));

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.CannotCarry);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AProposalInvalidatedByARosterChange_IsNotEnacted()
    {
        // FR-011b. Enacting it would apply a change judged against a pool that no longer exists.
        QuorumMet();
        _rosterService.Setup(r => r.GetCurrentRosterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AdminRoster
            {
                RegisterId = RegisterId,
                ControlRecord = Roster().ControlRecord,
                ControlTransactionCount = 2,
                LastControlTxId = "some-later-control-tx",
            });

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.NotEnactable);
        result.Detail.Should().Contain("some-later-control-tx");
    }

    [Fact]
    public async Task AProposalThatIsNoLongerOpen_IsNotEnacted()
    {
        QuorumMet();
        _reader.Setup(r => r.ReadAsync(RegisterId, ProposalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var decided = PendingOperation();
                decided.Status = ProposalStatus.Recorded;
                return new GovernanceProposalRead(ProposalReadOutcome.Found, decided, null);
            });

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.NotEnactable);
    }

    [Fact]
    public async Task AValidatorRefusal_IsReportedWithItsReason_NotThrown()
    {
        QuorumMet();
        _validator.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorMessage = "VAL_PERM_006: Quorum not met from sealed approvals: 1/2",
            });

        var result = await _sut.TryEnactAsync(RegisterId, ProposalId);

        result.Outcome.Should().Be(EnactmentOutcome.Refused);
        result.Detail.Should().Contain("VAL_PERM_006",
            "the Validator is the authority, and its refusal must reach an operator intact");
    }

    [Fact]
    public async Task TheAddedAttestationTakesItsTimestampFromTheProposal_NotTheClock()
    {
        QuorumMet();

        await _sut.TryEnactAsync(RegisterId, ProposalId);

        var payload = JsonSerializer.Deserialize<ControlTransactionPayload>(
            _captured!.Payload.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        payload.Roster!.Attestations
            .Should().Contain(a => a.Subject == "did:sorcha:w:ws11qnewadmin")
            .Which.GrantedAt.Should().Be(DateTimeOffset.UnixEpoch,
                "a clock read here breaks byte-determinism between nodes");
    }
}
