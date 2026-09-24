// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class ParticipantPublishingServiceTests
{
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly Mock<IValidatorServiceClient> _validatorClientMock;
    private readonly Mock<IWalletServiceClient> _walletClientMock;
    private readonly Mock<ILogger<ParticipantPublishingService>> _loggerMock;
    private readonly ParticipantPublishingService _service;

    public ParticipantPublishingServiceTests()
    {
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _validatorClientMock = new Mock<IValidatorServiceClient>();
        _walletClientMock = new Mock<IWalletServiceClient>();
        _loggerMock = new Mock<ILogger<ParticipantPublishingService>>();

        // Default: no existing control transactions
        _registerClientMock.Setup(r => r.GetControlTransactionsAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage { Page = 1, PageSize = 1 });

        // Default: wallet signing succeeds
        _walletClientMock.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = new byte[64],
                PublicKey = new byte[32],
                SignedBy = "test-wallet",
                Algorithm = "ED25519"
            });

        // Default: validator submission succeeds
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx",
                RegisterId = "test-register",
                AddedAt = DateTimeOffset.UtcNow
            });

        _service = new ParticipantPublishingService(
            _registerClientMock.Object,
            _validatorClientMock.Object,
            _walletClientMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        var act = () => new ParticipantPublishingService(
            null!, _validatorClientMock.Object, _walletClientMock.Object, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("registerClient");
    }

    [Fact]
    public void Constructor_WithNullValidatorClient_ThrowsArgumentNullException()
    {
        var act = () => new ParticipantPublishingService(
            _registerClientMock.Object, null!, _walletClientMock.Object, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("validatorClient");
    }

    [Fact]
    public void Constructor_WithNullWalletClient_ThrowsArgumentNullException()
    {
        var act = () => new ParticipantPublishingService(
            _registerClientMock.Object, _validatorClientMock.Object, null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("walletClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ParticipantPublishingService(
            _registerClientMock.Object, _validatorClientMock.Object, _walletClientMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region PublishParticipantAsync Tests

    [Fact]
    public async Task PublishParticipantAsync_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = await _service.PublishParticipantAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().NotBeNullOrWhiteSpace();
        result.ParticipantId.Should().NotBeNullOrWhiteSpace();
        result.RegisterId.Should().Be(request.RegisterId);
        result.Version.Should().Be(1);
        result.Status.Should().Be("submitted");
    }

    [Fact]
    public async Task PublishParticipantAsync_GeneratesUuidParticipantId()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = await _service.PublishParticipantAsync(request);

        // Assert
        Guid.TryParse(result.ParticipantId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task PublishParticipantAsync_BuildsDeterministicTxId()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        var result = await _service.PublishParticipantAsync(request);

        // Assert — TxId should be 64 hex characters (SHA256)
        result.TransactionId.Should().HaveLength(64);
        result.TransactionId.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task PublishParticipantAsync_SubmitsTransactionWithCorrectType()
    {
        // Arrange
        var request = CreateValidRequest();
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true,
                TransactionId = "test-tx",
                RegisterId = "test-register"
            });

        // Act
        await _service.PublishParticipantAsync(request);

        // Assert
        captured.Should().NotBeNull();
        captured!.Metadata.Should().ContainKey("Type");
        captured.Metadata!["Type"].Should().Be("Participant");
        captured.BlueprintId.Should().BeNull();
        captured.ActionId.Should().BeNull();
    }

    [Fact]
    public async Task PublishParticipantAsync_SubmitsTransactionWithPayload()
    {
        // Arrange
        var request = CreateValidRequest();
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.PublishParticipantAsync(request);

        // Assert — payload contains participant record fields
        captured.Should().NotBeNull();
        captured!.Payload.ValueKind.Should().Be(JsonValueKind.Object);
        captured.Payload.GetProperty("participantName").GetString().Should().Be(request.ParticipantName);
        captured.Payload.GetProperty("organizationName").GetString().Should().Be(request.OrganizationName);
        captured.Payload.GetProperty("status").GetString().Should().Be("Active");
        captured.Payload.GetProperty("version").GetInt32().Should().Be(1);
        captured.Payload.GetProperty("addresses").GetArrayLength().Should().Be(request.Addresses.Count);
    }

    [Fact]
    public async Task PublishParticipantAsync_SignsWithSpecifiedWallet()
    {
        // Arrange
        var request = CreateValidRequest(signerWallet: "my-wallet-address");

        // Act
        await _service.PublishParticipantAsync(request);

        // Assert
        _walletClientMock.Verify(w => w.SignTransactionAsync(
            "my-wallet-address",
            It.IsAny<byte[]>(),
            null,
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishParticipantAsync_IncludesSignatureInSubmission()
    {
        // Arrange
        var testSig = new byte[] { 1, 2, 3, 4, 5 };
        var testPubKey = new byte[] { 10, 20, 30 };

        _walletClientMock.Setup(w => w.SignTransactionAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletSignResult
            {
                Signature = testSig,
                PublicKey = testPubKey,
                SignedBy = "test",
                Algorithm = "ED25519"
            });

        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert
        captured!.Signatures.Should().HaveCount(1);
        captured.Signatures[0].PublicKey.Should().Be(Base64Url.EncodeToString(testPubKey));
        captured.Signatures[0].SignatureValue.Should().Be(Base64Url.EncodeToString(testSig));
        captured.Signatures[0].Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task PublishParticipantAsync_ChainsFromLatestControlTx()
    {
        // Arrange
        var controlTxId = "control-tx-abc123";
        _registerClientMock.Setup(r => r.GetControlTransactionsAsync(
                "test-register", 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1,
                PageSize = 1,
                Total = 1,
                Transactions = [ControlTx(controlTxId)]
            });

        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert
        captured!.PreviousTransactionId.Should().Be(controlTxId);
    }

    [Fact]
    public async Task PublishParticipantAsync_NoControlTx_ChainsFromNull()
    {
        // Arrange (default mock returns empty TransactionPage)
        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert
        captured!.PreviousTransactionId.Should().BeNull();
    }

    [Theory]
    [InlineData(Sorcha.Register.Models.Enums.TransactionType.Participant)]
    [InlineData(Sorcha.Register.Models.Enums.TransactionType.Action)]
    public async Task PublishParticipantAsync_WhenTheLatestIsNotAControlTx_ChainsFromNullRatherThanFromIt(
        Sorcha.Register.Models.Enums.TransactionType returnedType)
    {
        // The seam this fixes: a query named "control transactions" that returned the newest
        // transaction of ANY type. Chaining from such a transaction does not fail — it forks the
        // next write that also chains from it. Null is fork-safe; a non-Control parent is not.
        LatestControlTxIs("not-actually-control", returnedType);
        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        await _service.PublishParticipantAsync(CreateValidRequest());

        captured!.PreviousTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task PublishParticipantAsync_SubmitsToValidator()
    {
        // Arrange
        var request = CreateValidRequest();

        // Act
        await _service.PublishParticipantAsync(request);

        // Assert
        _validatorClientMock.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishParticipantAsync_ValidatorRejects_ThrowsInvalidOperationException()
    {
        // Arrange
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorMessage = "Schema validation failed",
                ErrorCode = "VAL_PARTICIPANT_001"
            });

        // Act
        var act = async () => await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Schema validation failed*");
    }

    [Fact]
    public async Task PublishParticipantAsync_NullRequest_ThrowsArgumentNullException()
    {
        var act = async () => await _service.PublishParticipantAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishParticipantAsync_PayloadHashIsValid()
    {
        // Arrange
        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert — PayloadHash should be 64 hex characters (SHA256)
        captured!.PayloadHash.Should().HaveLength(64);
        captured.PayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task PublishParticipantAsync_MetadataIncludesParticipantInfo()
    {
        // Arrange
        TransactionSubmission? captured = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        var result = await _service.PublishParticipantAsync(CreateValidRequest());

        // Assert
        captured!.Metadata.Should().ContainKey("participantId");
        captured.Metadata!["participantId"].Should().Be(result.ParticipantId);
        captured.Metadata.Should().ContainKey("version");
        captured.Metadata["version"].Should().Be("1");
    }

    #endregion

    #region UpdateParticipantAsync Tests

    [Fact]
    public async Task UpdateParticipantAsync_ValidRequest_IncrementsVersion()
    {
        // Arrange
        SetupExistingParticipant("part-1", version: 1, latestTxId: "prev-tx");

        var request = CreateValidUpdateRequest(participantId: "part-1");

        // Act
        var result = await _service.UpdateParticipantAsync(request);

        // Assert
        result.Version.Should().Be(2);
        result.ParticipantId.Should().Be("part-1");
    }

    [Fact]
    public async Task UpdateParticipantAsync_ChainsFromLatestControlTxNotItsPreviousVersion()
    {
        // The previous version is usually NOT a free parent: every participant published after it
        // chained off it, and the validator allows one successor per non-Control predecessor. So an
        // update chained from its previous version forked with VAL_CHAIN_FORK after the API had
        // already reported success (ConstructionPermit setup, n1, 2026-09-24). A Control
        // predecessor is exempt from the fork check, so it is always a valid parent.
        SetupExistingParticipant("part-1", version: 1, latestTxId: "existing-tx-id");
        LatestControlTxIs("control-tx-abc123");
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        await _service.UpdateParticipantAsync(CreateValidUpdateRequest(participantId: "part-1"));

        captured!.PreviousTransactionId.Should().Be("control-tx-abc123");
    }

    [Fact]
    public async Task UpdateParticipantAsync_PreservesParticipantId()
    {
        // Arrange
        SetupExistingParticipant("part-1", version: 1, latestTxId: "prev-tx");
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.UpdateParticipantAsync(CreateValidUpdateRequest(
            participantId: "part-1", participantName: "Updated Alice"));

        // Assert
        captured!.Payload.GetProperty("participantId").GetString().Should().Be("part-1");
        captured.Payload.GetProperty("participantName").GetString().Should().Be("Updated Alice");
        captured.Payload.GetProperty("version").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task UpdateParticipantAsync_ParticipantNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange — no existing participant
        _registerClientMock.Setup(r => r.GetPublishedParticipantByIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?)null);

        // Act
        var act = async () => await _service.UpdateParticipantAsync(
            CreateValidUpdateRequest(participantId: "nonexistent"));

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateParticipantAsync_NullRequest_ThrowsArgumentNullException()
    {
        var act = async () => await _service.UpdateParticipantAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateParticipantAsync_DeterministicTxIdWithNewVersion()
    {
        // Arrange
        SetupExistingParticipant("part-1", version: 3, latestTxId: "prev-tx");

        // Act
        var result = await _service.UpdateParticipantAsync(CreateValidUpdateRequest(participantId: "part-1"));

        // Assert
        result.Version.Should().Be(4);
        result.TransactionId.Should().HaveLength(64);
        result.TransactionId.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    #endregion

    #region RevokeParticipantAsync Tests

    [Fact]
    public async Task RevokeParticipantAsync_ValidRequest_SetsStatusRevoked()
    {
        // Arrange
        SetupExistingParticipant("part-1", version: 1, latestTxId: "prev-tx");
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.RevokeParticipantAsync(new RevokeParticipantRequest
        {
            RegisterId = "test-register",
            ParticipantId = "part-1",
            SignerWalletAddress = "signer-wallet"
        });

        // Assert
        captured!.Payload.GetProperty("status").GetString().Should().Be("Revoked");
        captured.Payload.GetProperty("version").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task RevokeParticipantAsync_PreservesExistingData()
    {
        // Arrange
        SetupExistingParticipant("part-1", version: 1, latestTxId: "prev-tx",
            participantName: "Alice", organizationName: "Acme");
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        // Act
        await _service.RevokeParticipantAsync(new RevokeParticipantRequest
        {
            RegisterId = "test-register",
            ParticipantId = "part-1",
            SignerWalletAddress = "signer-wallet"
        });

        // Assert
        captured!.Payload.GetProperty("participantName").GetString().Should().Be("Alice");
        captured.Payload.GetProperty("organizationName").GetString().Should().Be("Acme");
        captured.Payload.GetProperty("participantId").GetString().Should().Be("part-1");
    }

    [Fact]
    public async Task RevokeParticipantAsync_ChainsFromLatestControlTxNotItsCurrentVersion()
    {
        // Same fork as an update: revoking any participant but the last one published would
        // otherwise be refused, leaving a role that can never be retired.
        SetupExistingParticipant("part-1", version: 2, latestTxId: "v2-tx");
        LatestControlTxIs("control-tx-abc123");
        TransactionSubmission? captured = null;

        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => captured = sub)
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = "tx" });

        await _service.RevokeParticipantAsync(new RevokeParticipantRequest
        {
            RegisterId = "test-register",
            ParticipantId = "part-1",
            SignerWalletAddress = "signer-wallet"
        });

        captured!.PreviousTransactionId.Should().Be("control-tx-abc123");
    }

    [Fact]
    public async Task RevokeParticipantAsync_ParticipantNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _registerClientMock.Setup(r => r.GetPublishedParticipantByIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?)null);

        // Act
        var act = async () => await _service.RevokeParticipantAsync(new RevokeParticipantRequest
        {
            RegisterId = "test-register",
            ParticipantId = "nonexistent",
            SignerWalletAddress = "signer-wallet"
        });

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RevokeParticipantAsync_NullRequest_ThrowsArgumentNullException()
    {
        var act = async () => await _service.RevokeParticipantAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region ComputeTxId Tests

    [Fact]
    public void ComputeTxId_ReturnsDeterministicHash()
    {
        var txId1 = ParticipantPublishingService.ComputeTxId("reg-1", "part-1", 1);
        var txId2 = ParticipantPublishingService.ComputeTxId("reg-1", "part-1", 1);
        txId1.Should().Be(txId2);
    }

    [Fact]
    public void ComputeTxId_DifferentInputs_ReturnsDifferentHash()
    {
        var txId1 = ParticipantPublishingService.ComputeTxId("reg-1", "part-1", 1);
        var txId2 = ParticipantPublishingService.ComputeTxId("reg-1", "part-1", 2);
        txId1.Should().NotBe(txId2);
    }

    [Fact]
    public void ComputeTxId_ReturnsLowercaseHex64()
    {
        var txId = ParticipantPublishingService.ComputeTxId("reg-1", "part-1", 1);
        txId.Should().HaveLength(64);
        txId.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    #endregion

    #region Helper Methods

    #region Superseding an already-published role (#1668)

    /// <summary>
    /// Publishing a role that is already published is a CORRECTION, not a second binding.
    /// </summary>
    /// <remarks>
    /// Cold-start run #5: the second publish created a parallel Active record; resolution returned
    /// the older one, so the correction was silently ignored; and because the new record chained
    /// from the SAME prevTxId as the original, every later write against the original — revoke
    /// included — forked with VAL_CHAIN_FORK and was refused forever. A mis-bound role became
    /// permanent, recoverable only by abandoning the register.
    /// </remarks>
    [Fact]
    public async Task PublishParticipantAsync_WhenTheRoleIsAlreadyPublished_SupersedesItRatherThanDuplicating()
    {
        ExistingRole("participant-1", version: 1, latestTxId: "tx-v1");

        var result = await _service.PublishParticipantAsync(CreateValidRequest());

        // Same record, next version — not a new identity.
        result.ParticipantId.Should().Be("participant-1");
        result.Version.Should().Be(2);
    }

    [Fact]
    public async Task PublishParticipantAsync_WhenSuperseding_ChainsFromTheLatestControlTx()
    {
        // #1670 chained the correction from the existing record, reasoning that a Control parent
        // would fork. It does not: the validator exempts Control predecessors from the fork check.
        // What forked in run #5 was a "Control TX" that was really the newest transaction of any
        // type, because the query's type filter was never applied. The existing record is the
        // parent that forks, as soon as anything else has chained from it.
        ExistingRole("participant-1", version: 1, latestTxId: "tx-v1");
        LatestControlTxIs("control-tx-abc123");
        TransactionSubmission? submitted = null;
        _validatorClientMock.Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback((TransactionSubmission s, CancellationToken _) => submitted = s)
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = true, TransactionId = "tx-v2", RegisterId = "test-register",
                AddedAt = DateTimeOffset.UtcNow
            });

        await _service.PublishParticipantAsync(CreateValidRequest());

        submitted!.PreviousTransactionId.Should().Be("control-tx-abc123");
    }

    [Fact]
    public async Task PublishParticipantAsync_WhenNoRoleIsPublishedYet_StillCreatesAFirstRecord()
    {
        // The counterfactual: superseding must not swallow a genuine first publish.
        NoExistingRole();

        var result = await _service.PublishParticipantAsync(CreateValidRequest());

        result.Version.Should().Be(1);
        result.ParticipantId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PublishParticipantAsync_WhenTheExistingRoleWasRevoked_PublishesAFreshRecord()
    {
        // Revocation is how a role is retired, so re-publishing afterwards is a NEW binding.
        // Superseding a revoked record would resurrect the retirement as an active version.
        ExistingRole("participant-1", version: 3, latestTxId: "tx-v3", status: "Revoked");

        var result = await _service.PublishParticipantAsync(CreateValidRequest());

        result.Version.Should().Be(1);
        result.ParticipantId.Should().NotBe("participant-1");
    }

    [Fact]
    public async Task PublishParticipantAsync_WhenTheRegisterCannotBeConsulted_StillPublishes()
    {
        // A lookup failure must not block a first publish; address uniqueness still guards the
        // case that matters, and refusing here would make an unreachable register unusable.
        _registerClientMock.Setup(r => r.ResolveParticipantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("register unreachable"));

        var result = await _service.PublishParticipantAsync(CreateValidRequest());

        result.Version.Should().Be(1);
    }

    private void ExistingRole(string participantId, int version, string latestTxId, string status = "Active")
    {
        _registerClientMock.Setup(r => r.ResolveParticipantAsync(
                "test-register", "Alice", "Acme Corp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord
            {
                ParticipantId = participantId,
                ParticipantName = "Alice",
                OrganizationName = "Acme Corp",
                Status = status,
                Version = version,
                LatestTxId = latestTxId,
                Addresses = [],
            });

        // The update path re-reads the record by id to derive the next version.
        _registerClientMock.Setup(r => r.GetPublishedParticipantByIdAsync(
                "test-register", participantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord
            {
                ParticipantId = participantId,
                ParticipantName = "Alice",
                OrganizationName = "Acme Corp",
                Status = status,
                Version = version,
                LatestTxId = latestTxId,
                Addresses = [],
            });
    }

    private static Sorcha.Register.Models.TransactionModel ControlTx(
        string txId,
        Sorcha.Register.Models.Enums.TransactionType type = Sorcha.Register.Models.Enums.TransactionType.Control) =>
        new()
        {
            TxId = txId,
            MetaData = new Sorcha.Register.Models.TransactionMetaData { TransactionType = type },
        };

    private void LatestControlTxIs(
        string txId,
        Sorcha.Register.Models.Enums.TransactionType type = Sorcha.Register.Models.Enums.TransactionType.Control) =>
        _registerClientMock.Setup(r => r.GetControlTransactionsAsync(
                "test-register", 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionPage
            {
                Page = 1, PageSize = 1, Total = 1, Transactions = [ControlTx(txId, type)],
            });

    private void NoExistingRole() =>
        _registerClientMock.Setup(r => r.ResolveParticipantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord?)null);

    #endregion

    private static PublishParticipantRequest CreateValidRequest(
        string? registerId = null,
        string? signerWallet = null)
    {
        return new PublishParticipantRequest
        {
            RegisterId = registerId ?? "test-register",
            ParticipantName = "Alice",
            OrganizationName = "Acme Corp",
            Addresses =
            [
                new ParticipantAddressRequest
                {
                    WalletAddress = "addr-1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "ED25519",
                    Primary = true
                }
            ],
            SignerWalletAddress = signerWallet ?? "signer-wallet"
        };
    }

    private static UpdatePublishedParticipantRequest CreateValidUpdateRequest(
        string? participantId = null,
        string? participantName = null)
    {
        return new UpdatePublishedParticipantRequest
        {
            RegisterId = "test-register",
            ParticipantId = participantId ?? "part-1",
            ParticipantName = participantName ?? "Alice Updated",
            OrganizationName = "Acme Corp",
            Addresses =
            [
                new ParticipantAddressRequest
                {
                    WalletAddress = "addr-1",
                    PublicKey = Convert.ToBase64String(new byte[32]),
                    Algorithm = "ED25519",
                    Primary = true
                }
            ],
            SignerWalletAddress = "signer-wallet"
        };
    }

    private void SetupExistingParticipant(
        string participantId,
        int version,
        string latestTxId,
        string participantName = "Alice",
        string organizationName = "Acme Corp")
    {
        _registerClientMock.Setup(r => r.GetPublishedParticipantByIdAsync(
                "test-register", participantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.ServiceClients.Register.Models.PublishedParticipantRecord
            {
                ParticipantId = participantId,
                OrganizationName = organizationName,
                ParticipantName = participantName,
                Status = "Active",
                Version = version,
                LatestTxId = latestTxId,
                Addresses =
                [
                    new Sorcha.ServiceClients.Register.Models.ParticipantAddressInfo
                    {
                        WalletAddress = "addr-1",
                        PublicKey = Convert.ToBase64String(new byte[32]),
                        Algorithm = "ED25519",
                        Primary = true
                    }
                ]
            });
    }

    #endregion
}
