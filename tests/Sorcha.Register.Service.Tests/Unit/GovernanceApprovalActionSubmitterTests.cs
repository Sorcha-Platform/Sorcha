// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// T075 — an approval reaches the ledger as an action submission of the governance blueprint,
/// carried through the Validator.
/// </summary>
public sealed class GovernanceApprovalActionSubmitterTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string ProposalId = "360bf52a360bf52a360bf52a360bf52a";
    private const string ApproverDid = "did:sorcha:w:ws11qapprover";
    private const string CarrierSubject = "did:sorcha:w:ws11qowner";

    private static readonly byte[] CarrierSignature = [0xAA, 0xBB, 0xCC, 0xDD];
    private static readonly byte[] CarrierPublicKey = [0x01, 0x02, 0x03, 0x04];

    private readonly Mock<IGovernanceSigningService> _signing = new();
    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly GovernanceApprovalActionSubmitter _sut;

    private TransactionSubmission? _captured;
    private (string TxId, string PayloadHash, string? PreferredSubject)? _signedOver;

    public GovernanceApprovalActionSubmitterTests()
    {
        _signing
            .Setup(x => x.SignAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string txId, string hash, string? subject, CancellationToken _) =>
                _signedOver = (txId, hash, subject))
            .ReturnsAsync(new GovernanceSignResult
            {
                Signature = CarrierSignature,
                PublicKey = CarrierPublicKey,
                Algorithm = "ED25519",
                WalletAddress = "ws11qowner",
                Subject = CarrierSubject,
            });

        _validator
            .Setup(x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionSubmission s, CancellationToken _) => _captured = s)
            .ReturnsAsync(() => new TransactionSubmissionResult { Success = true });

        _sut = new GovernanceApprovalActionSubmitter(
            _signing.Object, _validator.Object,
            Mock.Of<ILogger<GovernanceApprovalActionSubmitter>>());
    }

    private static GovernanceApprovalSubmission Submission(bool isApproval = true) => new()
    {
        RequestId = ProposalId,
        ApproverDid = ApproverDid,
        IsApproval = isApproval,
        Signature = "b3JnLXNpZ25hdHVyZQ==",
        PublicKey = "b3JnLXB1YmxpYy1rZXk=",
        AuthMethod = ApprovalAuthMethod.HardwareBacked,
        Comment = "Roster diff reviewed.",
        Authorisation = new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Direct,
            IndividualDid = "did:sorcha:w:ws11qindividual",
            Signature = "aW5kaXZpZHVhbC1zaWc=",
            PublicKey = "aW5kaXZpZHVhbC1rZXk=",
            AuthMethod = ApprovalAuthMethod.HardwareBacked,
            Algorithm = SignatureAlgorithm.ED25519,
        },
    };

    [Fact]
    public async Task SubmitAsync_CarriesTheApprovalThroughTheValidator()
    {
        var result = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        result.Submitted.Should().BeTrue();
        result.Error.Should().BeNull();
        _validator.Verify(
            x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "writing a governance record straight to storage was the original US1 defect");
    }

    [Fact]
    public async Task SubmitAsync_RecordsItAgainstTheGovernanceBlueprintsApprovalAction()
    {
        await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        _captured!.BlueprintId.Should().Be(GovernanceBlueprint.BlueprintId);
        _captured.ActionId.Should().Be(GovernanceBlueprint.CollectQuorumActionId.ToString());
        _captured.RegisterId.Should().Be(RegisterId);
    }

    [Fact]
    public async Task SubmitAsync_ChainsTheApprovalOffTheProposalItAnswers()
    {
        // Mirrors the blueprint's action 1 → action 2 route. Sibling approvals are expected: the
        // Validator's fork check exempts a Control predecessor, which a proposal is.
        await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        _captured!.PreviousTransactionId.Should().Be(ProposalId);
    }

    [Fact]
    public async Task SubmitAsync_MarksItControl_SoTheValidatorAppliesTheRosterCheck()
    {
        await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        _captured!.Metadata.Should().Contain("Type", "Control");
        _captured.Metadata.Should().Contain(
            "transactionType", GovernanceApprovalActionSubmitter.GovernanceApprovalTrackingType);
        _captured.Metadata.Should().Contain("proposalId", ProposalId);
        _captured.Metadata.Should().Contain("approverDid", ApproverDid);
    }

    [Fact]
    public async Task SubmitAsync_TheEnvelopeSignatureIsTheCarriersNotTheApprovers()
    {
        // The one that matters most. An approver signs GovernanceApprovalStatement — the register,
        // the operation, who is approving, approve-or-reject — which is not a transaction digest,
        // because they sign before any transaction exists. Putting that signature in Signatures
        // fails VAL_SIG_002 every time, and the symptom ("invalid signature") points at the approver
        // rather than at the code that misfiled it.
        var submission = Submission();

        await _sut.SubmitAsync(RegisterId, ProposalId, submission);

        _captured!.Signatures.Should().ContainSingle();
        var envelope = _captured.Signatures[0];

        envelope.SignatureValue.Should().Be(Base64Url.EncodeToString(CarrierSignature));
        envelope.PublicKey.Should().Be(Base64Url.EncodeToString(CarrierPublicKey));
        envelope.SignatureValue.Should().NotBe(submission.Signature);
        envelope.PublicKey.Should().NotBe(submission.PublicKey);

        _captured.Metadata.Should().Contain("carriedBy", CarrierSubject,
            "the ledger must be able to tell who carried an approval from who gave it");
    }

    [Fact]
    public async Task SubmitAsync_SignsTheEnvelopeAsTheRegistersOwnGovernanceKey_NeverAsTheApprover()
    {
        // preferredSubject stays null deliberately. Signing as the approver whenever this node
        // happened to hold their key would dress a carry up as an approval — the server-side signing
        // R-014 withdrew — and would do it only sometimes, which is worse than always.
        await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        _signedOver!.Value.PreferredSubject.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_SignsOverExactlyTheTransactionIdAndPayloadHashItSubmits()
    {
        // Diverging here produces a signature that verifies nowhere and a rejection naming no cause.
        await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        _signedOver!.Value.TxId.Should().Be(_captured!.TransactionId);
        _signedOver.Value.PayloadHash.Should().Be(_captured.PayloadHash);

        var expected = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(_captured.Payload.GetRawText()))).ToLowerInvariant();
        _captured.PayloadHash.Should().Be(expected);
    }

    [Fact]
    public async Task SubmitAsync_CarriesTheApproversSignatureIntoThePayloadVerbatim()
    {
        // Re-encoding would change the bytes a later verifier decodes, and a signature that no longer
        // verifies is indistinguishable from a forged one.
        var submission = Submission();

        await _sut.SubmitAsync(RegisterId, ProposalId, submission);

        var payload = _captured!.Payload;
        payload.GetProperty("type").GetString()
            .Should().Be(GovernanceApprovalActionPayload.PayloadType);
        payload.GetProperty("proposalId").GetString().Should().Be(ProposalId);
        payload.GetProperty("approverDid").GetString().Should().Be(ApproverDid);
        payload.GetProperty("isApproval").GetBoolean().Should().BeTrue();
        payload.GetProperty("signature").GetString().Should().Be(submission.Signature);
        payload.GetProperty("publicKey").GetString().Should().Be(submission.PublicKey);
        payload.GetProperty("authMethod").GetString().Should().Be("hardware-backed");
        payload.GetProperty("statementVersion").GetString()
            .Should().Be(GovernanceApprovalStatement.StatementVersion);
        payload.GetProperty("authorisation").GetProperty("individualDid").GetString()
            .Should().Be(submission.Authorisation!.IndividualDid);
    }

    [Fact]
    public async Task SubmitAsync_ARepeatApproval_ReusesTheSameTransactionId()
    {
        var first = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());
        var second = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        second.TxId.Should().Be(first.TxId, "a resubmission must dedupe, not add a second vote");
    }

    [Fact]
    public async Task SubmitAsync_AReplayWithTheVoteFlipped_CollidesWithTheOriginalRatherThanAddingASecondVote()
    {
        // isApproval is not an input to the id on purpose: two ids would put an approval and a
        // rejection from one organisation on the ledger together, and the recount would have to
        // decide which counts.
        var approve = await _sut.SubmitAsync(RegisterId, ProposalId, Submission(isApproval: true));
        var reject = await _sut.SubmitAsync(RegisterId, ProposalId, Submission(isApproval: false));

        reject.TxId.Should().Be(approve.TxId);
    }

    [Fact]
    public async Task SubmitAsync_DifferentApprovers_GetDistinctTransactions()
    {
        var a = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        var other = Submission();
        other.ApproverDid = "did:sorcha:w:ws11qsecondorg";
        var b = await _sut.SubmitAsync(RegisterId, ProposalId, other);

        b.TxId.Should().NotBe(a.TxId);
    }

    [Fact]
    public async Task SubmitAsync_DifferentProposals_GetDistinctTransactions()
    {
        var a = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());
        var b = await _sut.SubmitAsync(RegisterId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Submission());

        b.TxId.Should().NotBe(a.TxId);
    }

    [Fact]
    public async Task SubmitAsync_ValidatorRefusal_IsReportedWithItsReason_NotThrown()
    {
        _validator
            .Setup(x => x.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorMessage = "VAL_PERM_002: no signature matches a roster member",
            });

        var result = await _sut.SubmitAsync(RegisterId, ProposalId, Submission());

        result.Submitted.Should().BeFalse();
        result.Error.Should().Contain("VAL_PERM_002");
        result.TxId.Should().NotBeNullOrWhiteSpace(
            "the id is deterministic, so a caller can look up or retry the same transaction");
    }

    [Fact]
    public void TheSubmitter_HoldsNoStorageDependency()
    {
        // Structural, not behavioural: the US1 defect was a governance record written straight to
        // Mongo — validated by nothing, sealed by nothing, replicated by nothing, and
        // indistinguishable from one that had been. It cannot come back through this class if there
        // is nothing here to write with.
        var parameters = typeof(GovernanceApprovalActionSubmitter)
            .GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        // Without this, the assertion below would pass over an empty set — the shape a vacuous guard
        // takes, and one this repo has been caught by twice.
        parameters.Should().NotBeEmpty();

        parameters.Should().NotContain(
            n => n.Contains("Repository", StringComparison.Ordinal)
                 || n.Contains("Store", StringComparison.Ordinal)
                 || n.Contains("DbContext", StringComparison.Ordinal));
    }
}
