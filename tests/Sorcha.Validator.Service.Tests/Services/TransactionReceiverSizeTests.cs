// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

public class TransactionReceiverSizeTests
{
    private readonly Mock<IMemPoolManager> _memPoolManagerMock = new();
    private readonly Mock<IValidationEngine> _validationEngineMock = new();
    private readonly Mock<ILogger<TransactionReceiver>> _loggerMock = new();

    private TransactionReceiver CreateReceiver(int maxSizeBytes = 4 * 1024 * 1024)
    {
        var config = new TransactionReceiverConfiguration
        {
            MaxTransactionSizeBytes = maxSizeBytes
        };
        var configOptions = new Mock<IOptions<TransactionReceiverConfiguration>>();
        configOptions.Setup(x => x.Value).Returns(config);

        return new TransactionReceiver(
            _memPoolManagerMock.Object,
            _validationEngineMock.Object,
            configOptions.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ReceiveTransactionAsync_UnderSizeLimit_NotRejectedForSize()
    {
        // Arrange
        var receiver = CreateReceiver(maxSizeBytes: 1000);
        var smallData = new byte[500]; // Under limit

        // Act
        var result = await receiver.ReceiveTransactionAsync(
            "abc123def456",
            smallData,
            "peer1");

        // Assert - not rejected for size (may fail for other reasons like deserialization)
        if (!result.Accepted && result.ValidationErrors != null)
        {
            result.ValidationErrors.Should().NotContain(e => e.Contains("TRANSACTION_TOO_LARGE"));
        }
    }

    [Fact]
    public async Task ReceiveTransactionAsync_OverSizeLimit_RejectedWithTransactionTooLarge()
    {
        // Arrange
        var receiver = CreateReceiver(maxSizeBytes: 1000);
        var largeData = new byte[1500]; // Over limit

        // Act
        var result = await receiver.ReceiveTransactionAsync(
            "abc123def456",
            largeData,
            "peer1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Should().Contain("TRANSACTION_TOO_LARGE");
    }

    [Fact]
    public async Task ReceiveTransactionAsync_ExactlyAtLimit_NotRejectedForSize()
    {
        // Arrange
        var receiver = CreateReceiver(maxSizeBytes: 1000);
        var exactData = new byte[1000]; // Exactly at limit

        // Act
        var result = await receiver.ReceiveTransactionAsync(
            "abc123def456",
            exactData,
            "peer1");

        // Assert - not rejected for size
        if (!result.Accepted && result.ValidationErrors != null)
        {
            result.ValidationErrors.Should().NotContain(e => e.Contains("TRANSACTION_TOO_LARGE"));
        }
    }

    [Fact]
    public void DefaultConfiguration_MaxTransactionSizeBytes_Is4MB()
    {
        // Verify the default config is 4MB
        var config = new TransactionReceiverConfiguration();
        config.MaxTransactionSizeBytes.Should().Be(4 * 1024 * 1024);
    }

    [Fact]
    public async Task ReceiveTransactionAsync_ConfigurableLimit_RespectsCustomValue()
    {
        // Arrange - Use a very small limit
        var receiver = CreateReceiver(maxSizeBytes: 100);
        var data = new byte[200];

        // Act
        var result = await receiver.ReceiveTransactionAsync(
            "abc123def456",
            data,
            "peer1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Should().Contain("TRANSACTION_TOO_LARGE");
    }

    [Fact]
    public async Task ReceiveTransactionAsync_OverSizeLimit_DoesNotCallValidationEngine()
    {
        // Arrange
        var receiver = CreateReceiver(maxSizeBytes: 100);
        var largeData = new byte[200];

        // Act
        await receiver.ReceiveTransactionAsync(
            "abc123def456",
            largeData,
            "peer1");

        // Assert - validation engine should NOT have been called
        _validationEngineMock.Verify(
            v => v.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Size check should prevent reaching validation engine");
    }
}
