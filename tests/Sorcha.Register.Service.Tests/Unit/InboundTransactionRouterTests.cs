// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services.Implementation;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Grpc;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="InboundTransactionRouter"/>.
/// Verifies bloom filter matching, gRPC notification dispatch, and transaction type filtering.
/// </summary>
public class InboundTransactionRouterTests
{
    private const string RegisterId = "test-register-001";
    private const string TransactionId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string SenderAddress = "sender-wallet-addr-001";
    private const long DocketNumber = 42;

    private readonly Mock<ILocalAddressIndex> _mockAddressIndex;
    private readonly Mock<IWalletNotificationClient> _mockWalletClient;
    private readonly Mock<ILogger<InboundTransactionRouter>> _mockLogger;
    private readonly InboundTransactionRouter _sut;

    public InboundTransactionRouterTests()
    {
        _mockAddressIndex = new Mock<ILocalAddressIndex>();
        _mockWalletClient = new Mock<IWalletNotificationClient>();
        _mockLogger = new Mock<ILogger<InboundTransactionRouter>>();

        _sut = new InboundTransactionRouter(
            _mockAddressIndex.Object,
            _mockWalletClient.Object,
            _mockLogger.Object);
    }

    private static TransactionMetaData CreateMetadata(
        string blueprintId = "bp-001",
        string instanceId = "inst-001",
        uint actionId = 3,
        uint nextActionId = 4)
    {
        return new TransactionMetaData
        {
            BlueprintId = blueprintId,
            InstanceId = instanceId,
            ActionId = actionId,
            NextActionId = nextActionId
        };
    }

    private void SetupBloomMatch(string address, bool isLocal)
    {
        _mockAddressIndex
            .Setup(a => a.MayContainAsync(RegisterId, address, It.IsAny<CancellationToken>()))
            .ReturnsAsync(isLocal);
    }

    private void SetupSuccessfulNotification()
    {
        _mockWalletClient
            .Setup(c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotifyInboundTransactionResponse
            {
                Success = true,
                Delivery = NotificationDelivery.RealTime,
                Message = "Delivered"
            });
    }

    #region Bloom Filter Match Tests

    [Fact]
    public async Task RouteTransactionAsync_BloomFilterMatch_TriggersGrpcCall()
    {
        // Arrange
        var recipientAddress = "recipient-addr-001";
        SetupBloomMatch(recipientAddress, isLocal: true);
        SetupSuccessfulNotification();

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { recipientAddress }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(1);
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r => r.RecipientAddress == recipientAddress),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteTransactionAsync_NoBloomMatch_SkipsNotification()
    {
        // Arrange
        var recipientAddress = "unknown-addr-001";
        SetupBloomMatch(recipientAddress, isLocal: false);

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { recipientAddress }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(0);
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Transaction Type Filtering Tests

    [Theory]
    [InlineData(TransactionType.Control)]
    [InlineData(TransactionType.Docket)]
    [InlineData(TransactionType.Participant)]
    public async Task RouteTransactionAsync_NonActionType_ReturnsZero(TransactionType transactionType)
    {
        // Arrange
        var recipientAddress = "recipient-addr-001";

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, transactionType,
            new[] { recipientAddress }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(0);

        // Bloom filter should never be queried for non-action types
        _mockAddressIndex.Verify(
            a => a.MayContainAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Multiple Recipients Tests

    [Fact]
    public async Task RouteTransactionAsync_MultipleRecipientsOneLocalMatch_CallsGrpcOnce()
    {
        // Arrange
        var localAddress = "local-addr-001";
        var remoteAddress1 = "remote-addr-001";
        var remoteAddress2 = "remote-addr-002";

        SetupBloomMatch(localAddress, isLocal: true);
        SetupBloomMatch(remoteAddress1, isLocal: false);
        SetupBloomMatch(remoteAddress2, isLocal: false);
        SetupSuccessfulNotification();

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { remoteAddress1, localAddress, remoteAddress2 }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(1);
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r => r.RecipientAddress == localAddress),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Empty/Null Recipient List Tests

    [Fact]
    public async Task RouteTransactionAsync_EmptyRecipientList_ReturnsZero()
    {
        // Arrange & Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            Array.Empty<string>(), SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(0);
        _mockAddressIndex.Verify(
            a => a.MayContainAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RouteTransactionAsync_NullOrEmptyAddressInList_IsSkipped(string? address)
    {
        // Arrange
        var validAddress = "valid-addr-001";
        SetupBloomMatch(validAddress, isLocal: true);
        SetupSuccessfulNotification();

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { address!, validAddress }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(1);

        // Bloom filter should only be checked for the valid address
        _mockAddressIndex.Verify(
            a => a.MayContainAsync(RegisterId, validAddress, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockAddressIndex.Verify(
            a => a.MayContainAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region gRPC Failure Resilience Tests

    [Fact]
    public async Task RouteTransactionAsync_GrpcFailureOnOneAddress_ContinuesProcessingOthers()
    {
        // Arrange
        var failingAddress = "failing-addr-001";
        var successAddress = "success-addr-001";

        SetupBloomMatch(failingAddress, isLocal: true);
        SetupBloomMatch(successAddress, isLocal: true);

        // First call throws, second succeeds
        _mockWalletClient
            .Setup(c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r => r.RecipientAddress == failingAddress),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("gRPC channel unavailable"));

        _mockWalletClient
            .Setup(c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r => r.RecipientAddress == successAddress),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotifyInboundTransactionResponse
            {
                Success = true,
                Delivery = NotificationDelivery.RealTime,
                Message = "Delivered"
            });

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { failingAddress, successAddress }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert — only the successful notification is counted
        result.Should().Be(1);

        // Both addresses should have been attempted
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RouteTransactionAsync_NotificationReturnsFalse_NotCounted()
    {
        // Arrange
        var address = "addr-with-failed-delivery";
        SetupBloomMatch(address, isLocal: true);

        _mockWalletClient
            .Setup(c => c.NotifyInboundTransactionAsync(
                It.IsAny<NotifyInboundTransactionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotifyInboundTransactionResponse
            {
                Success = false,
                Message = "User not found"
            });

        // Act
        var result = await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { address }, SenderAddress,
            CreateMetadata(), DocketNumber);

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Metadata Mapping Tests

    [Fact]
    public async Task RouteTransactionAsync_MetadataFieldsMappedCorrectly()
    {
        // Arrange
        var address = "recipient-addr-001";
        var metadata = CreateMetadata(
            blueprintId: "bp-test-999",
            instanceId: "inst-test-777",
            actionId: 10,
            nextActionId: 11);

        SetupBloomMatch(address, isLocal: true);
        SetupSuccessfulNotification();

        // Act
        await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { address }, SenderAddress,
            metadata, DocketNumber, isRecovery: true);

        // Assert
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r =>
                    r.RecipientAddress == address &&
                    r.TransactionId == TransactionId &&
                    r.RegisterId == RegisterId &&
                    r.DocketNumber == DocketNumber &&
                    r.BlueprintId == "bp-test-999" &&
                    r.InstanceId == "inst-test-777" &&
                    r.ActionId == 10 &&
                    r.NextActionId == 11 &&
                    r.SenderAddress == SenderAddress &&
                    r.IsRecovery == true &&
                    r.Timestamp != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteTransactionAsync_NullMetadata_MapsDefaultValues()
    {
        // Arrange
        var address = "recipient-addr-001";
        SetupBloomMatch(address, isLocal: true);
        SetupSuccessfulNotification();

        // Act
        await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { address }, null, null, DocketNumber);

        // Assert
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r =>
                    r.BlueprintId == string.Empty &&
                    r.InstanceId == string.Empty &&
                    r.ActionId == 0 &&
                    r.NextActionId == 0 &&
                    r.SenderAddress == string.Empty &&
                    r.IsRecovery == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RouteTransactionAsync_PartialMetadata_MapsProvidedFieldsAndDefaults()
    {
        // Arrange
        var address = "recipient-addr-001";
        var metadata = new TransactionMetaData
        {
            BlueprintId = "bp-partial",
            InstanceId = null,
            ActionId = 5,
            NextActionId = null
        };

        SetupBloomMatch(address, isLocal: true);
        SetupSuccessfulNotification();

        // Act
        await _sut.RouteTransactionAsync(
            RegisterId, TransactionId, TransactionType.Action,
            new[] { address }, SenderAddress,
            metadata, DocketNumber);

        // Assert
        _mockWalletClient.Verify(
            c => c.NotifyInboundTransactionAsync(
                It.Is<NotifyInboundTransactionRequest>(r =>
                    r.BlueprintId == "bp-partial" &&
                    r.InstanceId == string.Empty &&
                    r.ActionId == 5 &&
                    r.NextActionId == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
