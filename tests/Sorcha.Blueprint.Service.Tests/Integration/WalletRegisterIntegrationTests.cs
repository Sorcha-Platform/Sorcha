// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.Register.Models;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Integration tests for Wallet and Register service client interface behaviors.
/// Uses mocked service clients to test expected behaviors without infrastructure dependencies.
/// </summary>
public class WalletRegisterIntegrationTests
{
    private readonly Mock<IWalletServiceClient> _mockWalletClient;
    private readonly Mock<IRegisterServiceClient> _mockRegisterClient;

    public WalletRegisterIntegrationTests()
    {
        _mockWalletClient = new Mock<IWalletServiceClient>();
        _mockRegisterClient = new Mock<IRegisterServiceClient>();
    }

    #region Wallet Service Client Tests

    [Fact]
    public async Task WalletServiceClient_EncryptPayload_ReturnsEncryptedData()
    {
        // Arrange
        var recipientWallet = "wallet123";
        var payload = Encoding.UTF8.GetBytes("test payload");
        var expectedEncrypted = Encoding.UTF8.GetBytes("encrypted-data");

        _mockWalletClient
            .Setup(x => x.EncryptPayloadAsync(recipientWallet, payload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEncrypted);

        // Act
        var result = await _mockWalletClient.Object.EncryptPayloadAsync(recipientWallet, payload);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedEncrypted);
    }

    [Fact]
    public async Task WalletServiceClient_DecryptPayload_ReturnsDecryptedData()
    {
        // Arrange
        var walletAddress = "wallet123";
        var encryptedPayload = Encoding.UTF8.GetBytes("encrypted-data");
        var expectedDecrypted = Encoding.UTF8.GetBytes("decrypted-data");

        _mockWalletClient
            .Setup(x => x.DecryptPayloadAsync(walletAddress, encryptedPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDecrypted);

        // Act
        var result = await _mockWalletClient.Object.DecryptPayloadAsync(walletAddress, encryptedPayload);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(expectedDecrypted);
    }

    [Fact]
    public async Task WalletServiceClient_SignTransaction_ReturnsSignature()
    {
        // Arrange
        var walletAddress = "wallet123";
        var transactionData = Encoding.UTF8.GetBytes("transaction-to-sign");
        var expectedSignature = Encoding.UTF8.GetBytes("signature-data");
        var expectedPublicKey = Encoding.UTF8.GetBytes("public-key-hex");
        var expectedSignResult = new WalletSignResult
        {
            Signature = expectedSignature,
            PublicKey = expectedPublicKey,
            SignedBy = walletAddress,
            Algorithm = "ED25519"
        };

        _mockWalletClient
            .Setup(x => x.SignTransactionAsync(walletAddress, transactionData, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSignResult);

        // Act
        var result = await _mockWalletClient.Object.SignTransactionAsync(walletAddress, transactionData);

        // Assert
        result.Should().NotBeNull();
        result.Signature.Should().BeEquivalentTo(expectedSignature);
        result.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public async Task WalletServiceClient_GetWallet_ReturnsWalletInfo()
    {
        // Arrange
        var walletAddress = "wallet123";
        var expectedWallet = new WalletInfo
        {
            Address = walletAddress,
            Name = "Test Wallet",
            PublicKey = "public-key-hex",
            Algorithm = "NIST P-256",
            Status = "Active",
            Owner = "user123",
            Tenant = "tenant123",
            CreatedAt = DateTime.UtcNow.AddDays(-7),
            UpdatedAt = DateTime.UtcNow
        };

        _mockWalletClient
            .Setup(x => x.GetWalletAsync(walletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedWallet);

        // Act
        var result = await _mockWalletClient.Object.GetWalletAsync(walletAddress);

        // Assert
        result.Should().NotBeNull();
        result!.Address.Should().Be(walletAddress);
        result.Name.Should().Be("Test Wallet");
        result.Status.Should().Be("Active");
    }

    [Fact]
    public async Task WalletServiceClient_GetWallet_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var walletAddress = "nonexistent-wallet";

        _mockWalletClient
            .Setup(x => x.GetWalletAsync(walletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        // Act
        var result = await _mockWalletClient.Object.GetWalletAsync(walletAddress);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Register Service Client Tests

    [Fact]
    public async Task RegisterServiceClient_SubmitTransaction_ReturnsTransaction()
    {
        // Arrange
        var registerId = "register123";
        var transaction = new TransactionModel
        {
            TxId = "tx-123",
            RegisterId = registerId,
            SenderWallet = "wallet-sender",
            TimeStamp = DateTime.UtcNow
        };

        _mockRegisterClient
            .Setup(x => x.SubmitTransactionAsync(registerId, transaction, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        // Act
        var result = await _mockRegisterClient.Object.SubmitTransactionAsync(registerId, transaction);

        // Assert
        result.Should().NotBeNull();
        result.TxId.Should().Be("tx-123");
        result.RegisterId.Should().Be(registerId);
    }

    [Fact]
    public async Task RegisterServiceClient_GetTransaction_ReturnsTransaction()
    {
        // Arrange
        var registerId = "register123";
        var transactionId = "tx-123";
        var expectedTransaction = new TransactionModel
        {
            TxId = transactionId,
            RegisterId = registerId,
            SenderWallet = "wallet-sender",
            TimeStamp = DateTime.UtcNow
        };

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(registerId, transactionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTransaction);

        // Act
        var result = await _mockRegisterClient.Object.GetTransactionAsync(registerId, transactionId);

        // Assert
        result.Should().NotBeNull();
        result!.TxId.Should().Be(transactionId);
        result.RegisterId.Should().Be(registerId);
    }

    [Fact]
    public async Task RegisterServiceClient_GetTransaction_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var registerId = "register123";
        var transactionId = "nonexistent-tx";

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(registerId, transactionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        // Act
        var result = await _mockRegisterClient.Object.GetTransactionAsync(registerId, transactionId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RegisterServiceClient_GetRegister_ReturnsRegister()
    {
        // Arrange
        var registerId = "register123";
        var expectedRegister = new Register.Models.Register
        {
            Id = registerId,
            Name = "Test Register",
            Status = Register.Models.Enums.RegisterStatus.Online
        };

        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRegister);

        // Act
        var result = await _mockRegisterClient.Object.GetRegisterAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(registerId);
        result.Name.Should().Be("Test Register");
    }

    [Fact]
    public async Task RegisterServiceClient_GetRegister_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var registerId = "nonexistent-register";

        _mockRegisterClient
            .Setup(x => x.GetRegisterAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Register.Models.Register?)null);

        // Act
        var result = await _mockRegisterClient.Object.GetRegisterAsync(registerId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region End-to-End Integration Tests

    [Fact]
    public async Task EndToEnd_PayloadEncryptionAndDecryption_WorksCorrectly()
    {
        // Arrange
        var walletAddress = "wallet123";
        var originalPayload = Encoding.UTF8.GetBytes("sensitive data");
        var encryptedData = Encoding.UTF8.GetBytes("encrypted-sensitive-data");

        _mockWalletClient
            .Setup(x => x.EncryptPayloadAsync(walletAddress, originalPayload, It.IsAny<CancellationToken>()))
            .ReturnsAsync(encryptedData);

        _mockWalletClient
            .Setup(x => x.DecryptPayloadAsync(walletAddress, encryptedData, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalPayload);

        // Act - Encrypt
        var encrypted = await _mockWalletClient.Object.EncryptPayloadAsync(walletAddress, originalPayload);

        // Act - Decrypt
        var decrypted = await _mockWalletClient.Object.DecryptPayloadAsync(walletAddress, encrypted);

        // Assert
        decrypted.Should().NotBeNull();
        decrypted.Should().BeEquivalentTo(originalPayload);
    }

    [Fact]
    public async Task EndToEnd_SubmitAndRetrieveTransaction_WorksCorrectly()
    {
        // Arrange
        var registerId = "register123";
        var transaction = new TransactionModel
        {
            TxId = "tx-456",
            RegisterId = registerId,
            SenderWallet = "wallet-sender",
            TimeStamp = DateTime.UtcNow
        };

        _mockRegisterClient
            .Setup(x => x.SubmitTransactionAsync(registerId, transaction, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        _mockRegisterClient
            .Setup(x => x.GetTransactionAsync(registerId, "tx-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(transaction);

        // Act - Submit
        var submitted = await _mockRegisterClient.Object.SubmitTransactionAsync(registerId, transaction);

        // Act - Retrieve
        var retrieved = await _mockRegisterClient.Object.GetTransactionAsync(registerId, "tx-456");

        // Assert
        submitted.Should().NotBeNull();
        retrieved.Should().NotBeNull();
        retrieved!.TxId.Should().Be("tx-456");
        retrieved.RegisterId.Should().Be(registerId);
    }

    #endregion
}
