// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Register.Models;
using System.Text;
using System.Text.Json;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;
using RouteModel = Sorcha.Blueprint.Models.Route;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for StateReconstructionService
/// </summary>
public class StateReconstructionServiceTests
{
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<Sorcha.Cryptography.Interfaces.ISymmetricCrypto> _mockSymmetricCrypto;
    private readonly Mock<ILogger<StateReconstructionService>> _mockLogger;
    private readonly StateReconstructionService _service;

    public StateReconstructionServiceTests()
    {
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockSymmetricCrypto = new Mock<Sorcha.Cryptography.Interfaces.ISymmetricCrypto>();
        _mockLogger = new Mock<ILogger<StateReconstructionService>>();

        _service = new StateReconstructionService(
            _mockRegisterClient.Object,
            _mockWalletClient.Object,
            _mockSymmetricCrypto.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullRegisterClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new StateReconstructionService(
                null!,
                _mockWalletClient.Object,
                _mockSymmetricCrypto.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullWalletClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new StateReconstructionService(
                _mockRegisterClient.Object,
                null!,
                _mockSymmetricCrypto.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullSymmetricCrypto_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new StateReconstructionService(
                _mockRegisterClient.Object,
                _mockWalletClient.Object,
                null!,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new StateReconstructionService(
                _mockRegisterClient.Object,
                _mockWalletClient.Object,
                _mockSymmetricCrypto.Object,
                null!));
    }

    #endregion

    #region ReconstructAsync Tests

    [Fact]
    public async Task ReconstructAsync_WithNoTransactions_ReturnsEmptyState()
    {
        // Arrange
        var blueprint = CreateTestBlueprint();
        var instanceId = "test-instance";
        var currentActionId = 2;
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant",
            ["officer"] = "wallet-officer"
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>());

        // Act
        var result = await _service.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        result.ActionCount.Should().Be(0);
        result.ActionData.Should().BeEmpty();
        result.PreviousTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task ReconstructAsync_WithNonExistentAction_ThrowsInvalidOperationException()
    {
        // Arrange
        var blueprint = CreateTestBlueprint();
        var instanceId = "test-instance";
        var currentActionId = 999; // Non-existent
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ReconstructAsync(
                blueprint,
                instanceId,
                currentActionId,
                registerId,
                delegationToken,
                participantWallets));
    }

    [Fact]
    public async Task ReconstructAsync_WithTransactions_AccumulatesState()
    {
        // Arrange
        var blueprint = CreateTestBlueprintWithRoutes();
        var instanceId = "test-instance";
        var currentActionId = 2;
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant",
            ["officer"] = "wallet-officer"
        };

        var tx1Data = JsonSerializer.SerializeToUtf8Bytes(new { loanAmount = 50000, applicantName = "John Doe" });
        var tx1Encrypted = Encoding.UTF8.GetBytes("encrypted-tx1-data");

        var transactions = new List<TransactionModel>
        {
            new TransactionModel
            {
                TxId = "tx-001",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-10),
                MetaData = new TransactionMetaData { ActionId = 1 },
                Payloads = new[]
                {
                    new PayloadModel
                    {
                        Data = Convert.ToBase64String(tx1Encrypted),
                        WalletAccess = new[] { "wallet-applicant" }
                    }
                }
            }
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockWalletClient
            .Setup(x => x.DecryptWithDelegationAsync(
                "wallet-applicant",
                tx1Encrypted,
                delegationToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx1Data);

        // Act
        var result = await _service.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        result.ActionCount.Should().Be(1);
        result.ActionData.Should().ContainKey("1");
        result.PreviousTransactionId.Should().Be("tx-001");

        // Verify decryption was called
        _mockWalletClient.Verify(
            x => x.DecryptWithDelegationAsync(
                "wallet-applicant",
                It.IsAny<byte[]>(),
                delegationToken,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconstructAsync_WithDisclosureGroupEnvelope_DecryptsAsLocalParticipant()
    {
        // Feature 137 — the production transaction format is the disclosure-group envelope
        // (Data = JSON { …, "encryptedPayloads": [ { ciphertext, nonce, wrappedKeys:[…] } ] }).
        // Reconstruction must find the group sealed to a local participant wallet, unwrap its
        // symmetric key via the Wallet Service, and symmetric-decrypt the group. This is what
        // lets the owner node recover a cross-node citizen's action-1 payload.

        // Arrange
        var blueprint = CreateTestBlueprintWithRoutes();
        var instanceId = "test-instance";
        var currentActionId = 2;
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant",
            ["officer"] = "wallet-officer"   // the acting participant on the owner node
        };

        var wrappedKeyBytes = Encoding.UTF8.GetBytes("wrapped-symmetric-key");
        var symmetricKeyBytes = Encoding.UTF8.GetBytes("the-unwrapped-symmetric-key-3232");
        var ciphertextBytes = Encoding.UTF8.GetBytes("group-ciphertext");
        var nonceBytes = Encoding.UTF8.GetBytes("group-nonce-24bytes-padxxx");
        var plaintextJson = JsonSerializer.SerializeToUtf8Bytes(new { loanAmount = 50000, applicantName = "John Doe" });

        // Build the on-register disclosure-group envelope, sealed to the officer wallet.
        var envelope = new
        {
            type = "action",
            contentEncoding = "encrypted",
            encryptedPayloads = new[]
            {
                new
                {
                    groupId = "g1",
                    disclosedFields = new[] { "loanAmount", "applicantName" },
                    ciphertext = Convert.ToBase64String(ciphertextBytes),
                    nonce = Convert.ToBase64String(nonceBytes),
                    encryptionAlgorithm = "XCHACHA20_POLY1305",
                    wrappedKeys = new[]
                    {
                        new
                        {
                            walletAddress = "wallet-officer",
                            encryptedKey = Convert.ToBase64String(wrappedKeyBytes),
                            algorithm = "ED25519"
                        }
                    }
                }
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

        var transactions = new List<TransactionModel>
        {
            new TransactionModel
            {
                TxId = "tx-001",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-10),
                MetaData = new TransactionMetaData { ActionId = 1 },
                Payloads = new[]
                {
                    // WalletAccess intentionally empty — the disclosure-group format does not
                    // populate it (this is exactly the cross-node case that used to fail).
                    new PayloadModel
                    {
                        Data = Convert.ToBase64String(envelopeBytes),
                        WalletAccess = Array.Empty<string>()
                    }
                }
            }
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Unwrap the symmetric key for the officer wallet (service-to-service unwrap).
        _mockWalletClient
            .Setup(x => x.DecryptWithDelegationAsync(
                "wallet-officer",
                It.Is<byte[]>(b => b.SequenceEqual(wrappedKeyBytes)),
                delegationToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(symmetricKeyBytes);

        // Symmetric-decrypt the group ciphertext → plaintext action-1 payload.
        _mockSymmetricCrypto
            .Setup(x => x.DecryptAsync(It.IsAny<Sorcha.Cryptography.Models.SymmetricCiphertext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sorcha.Cryptography.Models.CryptoResult<byte[]>.Success(plaintextJson));

        // Act
        var result = await _service.ReconstructAsync(
            blueprint, instanceId, currentActionId, registerId, delegationToken, participantWallets);

        // Assert — action 1's payload was recovered via the disclosure-group path.
        result.Should().NotBeNull();
        result.ActionCount.Should().Be(1);
        result.ActionData.Should().ContainKey("1");
        result.ActionData["1"].GetProperty("loanAmount").GetInt32().Should().Be(50000);
        result.PreviousTransactionId.Should().Be("tx-001");

        _mockWalletClient.Verify(
            x => x.DecryptWithDelegationAsync("wallet-officer", It.IsAny<byte[]>(), delegationToken, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconstructAsync_WithFirstAction_ReturnsEmptyStateWithNoDataNeeded()
    {
        // Arrange
        var blueprint = CreateTestBlueprintWithRoutes();
        var instanceId = "test-instance";
        var currentActionId = 1; // First action - no prior actions needed
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant"
        };

        // Setup register mock to return empty transactions (still needs to be called even for first action)
        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>());

        // Act
        var result = await _service.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        result.ActionCount.Should().Be(0);
    }

    [Fact]
    public async Task ReconstructAsync_WithMultipleTransactions_OrdersByTimestamp()
    {
        // Arrange
        var blueprint = CreateThreeActionBlueprintWithRoutes();
        var instanceId = "test-instance";
        var currentActionId = 3;
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant",
            ["reviewer"] = "wallet-reviewer"
        };

        var tx1Data = JsonSerializer.SerializeToUtf8Bytes(new { step = "one" });
        var tx2Data = JsonSerializer.SerializeToUtf8Bytes(new { step = "two" });

        var transactions = new List<TransactionModel>
        {
            new TransactionModel
            {
                TxId = "tx-002",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-5),
                MetaData = new TransactionMetaData { ActionId = 2 },
                Payloads = new[] { new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("enc2")), WalletAccess = new[] { "wallet-reviewer" } } }
            },
            new TransactionModel
            {
                TxId = "tx-001",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-10),
                MetaData = new TransactionMetaData { ActionId = 1 },
                Payloads = new[] { new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("enc1")), WalletAccess = new[] { "wallet-applicant" } } }
            }
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockWalletClient
            .Setup(x => x.DecryptWithDelegationAsync(It.IsAny<string>(), It.IsAny<byte[]>(), delegationToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string wallet, byte[] data, string token, CancellationToken ct) =>
                wallet == "wallet-applicant" ? tx1Data : tx2Data);

        // Act
        var result = await _service.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        // The most recent transaction ID should be tx-002 (later timestamp after sorting)
        result.PreviousTransactionId.Should().Be("tx-002");
        result.ActionData.Should().ContainKey("1");
        result.ActionData.Should().ContainKey("2");
    }

    [Fact]
    public async Task ReconstructAsync_WithDecryptionFailure_ContinuesWithOtherTransactions()
    {
        // Arrange
        var blueprint = CreateThreeActionBlueprintWithRoutes();
        var instanceId = "test-instance";
        var currentActionId = 3;
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant",
            ["reviewer"] = "wallet-reviewer"
        };

        var tx2Data = JsonSerializer.SerializeToUtf8Bytes(new { step = "two" });

        var transactions = new List<TransactionModel>
        {
            new TransactionModel
            {
                TxId = "tx-001",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-10),
                MetaData = new TransactionMetaData { ActionId = 1 },
                Payloads = new[] { new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("enc1")), WalletAccess = new[] { "wallet-applicant" } } }
            },
            new TransactionModel
            {
                TxId = "tx-002",
                RegisterId = registerId,
                TimeStamp = DateTime.UtcNow.AddMinutes(-5),
                MetaData = new TransactionMetaData { ActionId = 2 },
                Payloads = new[] { new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("enc2")), WalletAccess = new[] { "wallet-reviewer" } } }
            }
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // First decryption fails
        _mockWalletClient
            .Setup(x => x.DecryptWithDelegationAsync("wallet-applicant", It.IsAny<byte[]>(), delegationToken, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Decryption failed"));

        // Second decryption succeeds
        _mockWalletClient
            .Setup(x => x.DecryptWithDelegationAsync("wallet-reviewer", It.IsAny<byte[]>(), delegationToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx2Data);

        // Act
        var result = await _service.ReconstructAsync(
            blueprint,
            instanceId,
            currentActionId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        result.ActionCount.Should().Be(1); // Only one successful decryption
        result.ActionData.Should().ContainKey("2");
        result.ActionData.Should().NotContainKey("1");
    }

    #endregion

    #region ReconstructForBranchAsync Tests

    [Fact]
    public async Task ReconstructForBranchAsync_WithValidBranch_ReturnsStateWithBranchContext()
    {
        // Arrange
        var blueprint = CreateTestBlueprint();
        var instanceId = "test-instance";
        var currentActionId = 2;
        var branchId = "branch-a";
        var registerId = "test-register";
        var delegationToken = "test-delegation-token";
        var participantWallets = new Dictionary<string, string>
        {
            ["applicant"] = "wallet-applicant"
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionsByInstanceIdAsync(registerId, instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>());

        // Act
        var result = await _service.ReconstructForBranchAsync(
            blueprint,
            instanceId,
            currentActionId,
            branchId,
            registerId,
            delegationToken,
            participantWallets);

        // Assert
        result.Should().NotBeNull();
        result.BranchStates.Should().ContainKey(branchId);
        result.BranchStates[branchId].Should().Be(BranchState.Active);
    }

    #endregion

    #region Helper Methods

    private static BlueprintModel CreateTestBlueprint()
    {
        return new BlueprintModel
        {
            Id = "test-blueprint",
            Title = "Test Blueprint",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new ParticipantModel { Id = "officer", Name = "Officer", WalletAddress = "wallet-officer" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel { Id = 1, Title = "Submit Application", Sender = "applicant" },
                new ActionModel { Id = 2, Title = "Review Application", Sender = "officer" }
            }
        };
    }

    private static BlueprintModel CreateTestBlueprintWithRoutes()
    {
        return new BlueprintModel
        {
            Id = "test-blueprint",
            Title = "Test Blueprint",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new ParticipantModel { Id = "officer", Name = "Officer", WalletAddress = "wallet-officer" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Application",
                    Sender = "applicant",
                    Routes = new List<RouteModel>
                    {
                        new RouteModel { NextActionIds = new List<int> { 2 } }
                    }
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Sender = "officer"
                }
            }
        };
    }

    private static BlueprintModel CreateThreeActionBlueprintWithRoutes()
    {
        return new BlueprintModel
        {
            Id = "test-blueprint",
            Title = "Test Blueprint",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel { Id = "applicant", Name = "Applicant", WalletAddress = "wallet-applicant" },
                new ParticipantModel { Id = "reviewer", Name = "Reviewer", WalletAddress = "wallet-reviewer" },
                new ParticipantModel { Id = "approver", Name = "Approver", WalletAddress = "wallet-approver" }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit",
                    Sender = "applicant",
                    Routes = new List<RouteModel>
                    {
                        new RouteModel { NextActionIds = new List<int> { 2 } }
                    }
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review",
                    Sender = "reviewer",
                    Routes = new List<RouteModel>
                    {
                        new RouteModel { NextActionIds = new List<int> { 3 } }
                    }
                },
                new ActionModel
                {
                    Id = 3,
                    Title = "Approve",
                    Sender = "approver"
                }
            }
        };
    }

    #endregion
}
