// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

public class TransactionReceiverTests
{
    private readonly Mock<IMemPoolManager> _memPoolManagerMock;
    private readonly Mock<IValidationEngine> _validationEngineMock;
    private readonly Mock<IOptionsMonitor<TransactionReceiverConfiguration>> _configMock;
    private readonly Mock<ILogger<TransactionReceiver>> _loggerMock;
    private readonly TransactionReceiverConfiguration _config;
    private readonly TransactionReceiver _receiver;

    public TransactionReceiverTests()
    {
        _config = new TransactionReceiverConfiguration
        {
            CleanupInterval = TimeSpan.FromMinutes(5),
            KnownTransactionRetention = TimeSpan.FromHours(1)
        };

        _memPoolManagerMock = new Mock<IMemPoolManager>();
        _validationEngineMock = new Mock<IValidationEngine>();
        _configMock = new Mock<IOptionsMonitor<TransactionReceiverConfiguration>>();
        _configMock.Setup(x => x.CurrentValue).Returns(_config);
        _loggerMock = new Mock<ILogger<TransactionReceiver>>();

        _receiver = new TransactionReceiver(
            _memPoolManagerMock.Object,
            _validationEngineMock.Object,
            _configMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullMemPoolManager_ThrowsArgumentNullException()
    {
        var act = () => new TransactionReceiver(
            null!,
            _validationEngineMock.Object,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("memPoolManager");
    }

    [Fact]
    public void Constructor_NullValidationEngine_ThrowsArgumentNullException()
    {
        var act = () => new TransactionReceiver(
            _memPoolManagerMock.Object,
            null!,
            _configMock.Object,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("validationEngine");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new TransactionReceiver(
            _memPoolManagerMock.Object,
            _validationEngineMock.Object,
            _configMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region ReceiveTransactionAsync Tests

    [Fact]
    public async Task ReceiveTransactionAsync_NullHash_ThrowsArgumentException()
    {
        // Arrange
        var data = CreateTransactionBytes("tx-1");

        // Act
        var act = () => _receiver.ReceiveTransactionAsync(null!, data, "peer-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReceiveTransactionAsync_EmptyHash_ThrowsArgumentException()
    {
        // Arrange
        var data = CreateTransactionBytes("tx-1");

        // Act
        var act = () => _receiver.ReceiveTransactionAsync(string.Empty, data, "peer-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReceiveTransactionAsync_NullData_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _receiver.ReceiveTransactionAsync("hash-1", null!, "peer-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReceiveTransactionAsync_InvalidData_ReturnsRejected()
    {
        // Arrange
        var invalidData = Encoding.UTF8.GetBytes("not valid json");

        // Act
        var result = await _receiver.ReceiveTransactionAsync("hash-1", invalidData, "peer-1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ReceiveTransactionAsync_HashMismatch_ReturnsRejected()
    {
        // Arrange
        var data = CreateTransactionBytes("tx-1", "different-hash");

        // Act
        var result = await _receiver.ReceiveTransactionAsync("expected-hash", data, "peer-1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().Contain("Transaction hash mismatch");
    }

    [Fact]
    public async Task ReceiveTransactionAsync_FailedValidation_ReturnsRejected()
    {
        // Arrange
        const string hash = "hash-tx1";
        var data = CreateTransactionBytes("tx-1", hash);

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult
            {
                TransactionId = "tx-1",
                RegisterId = "register-1",
                IsValid = false,
                Errors = new List<ValidationEngineError>
                {
                    new() { Code = "INVALID", Message = "Transaction invalid", Category = ValidationErrorCategory.Structure }
                }
            });

        // Act
        var result = await _receiver.ReceiveTransactionAsync(hash, data, "peer-1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().Contain("Transaction invalid");
    }

    [Fact]
    public async Task ReceiveTransactionAsync_MemPoolRejects_ReturnsRejected()
    {
        // Arrange
        const string hash = "hash-tx1";
        var data = CreateTransactionBytes("tx-1", hash);

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult { TransactionId = "tx-1", RegisterId = "register-1", IsValid = true });

        _memPoolManagerMock.Setup(x => x.AddTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _receiver.ReceiveTransactionAsync(hash, data, "peer-1");

        // Assert
        result.Accepted.Should().BeFalse();
        result.ValidationErrors.Should().Contain(e => e.Contains("memory pool"));
    }

    [Fact]
    public async Task ReceiveTransactionAsync_ValidTransaction_ReturnsAccepted()
    {
        // Arrange
        const string hash = "hash-tx1";
        var data = CreateTransactionBytes("tx-1", hash);

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult { TransactionId = "tx-1", RegisterId = "register-1", IsValid = true });

        _memPoolManagerMock.Setup(x => x.AddTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _receiver.ReceiveTransactionAsync(hash, data, "peer-1");

        // Assert
        result.Accepted.Should().BeTrue();
        result.TransactionId.Should().Be("tx-1");
    }

    [Fact]
    public async Task ReceiveTransactionAsync_DuplicateTransaction_ReturnsDuplicate()
    {
        // Arrange
        const string hash = "hash-tx1";
        var data = CreateTransactionBytes("tx-1", hash);

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult { TransactionId = "tx-1", RegisterId = "register-1", IsValid = true });

        _memPoolManagerMock.Setup(x => x.AddTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // First receive - should succeed
        var firstResult = await _receiver.ReceiveTransactionAsync(hash, data, "peer-1");
        firstResult.Accepted.Should().BeTrue();

        // Act - second receive with same hash
        var secondResult = await _receiver.ReceiveTransactionAsync(hash, data, "peer-2");

        // Assert
        secondResult.Accepted.Should().BeFalse();
        secondResult.AlreadyKnown.Should().BeTrue();
    }

    #endregion

    #region IsTransactionKnownAsync Tests

    [Fact]
    public async Task IsTransactionKnownAsync_NullHash_ThrowsArgumentException()
    {
        // Act
        var act = () => _receiver.IsTransactionKnownAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IsTransactionKnownAsync_UnknownHash_ReturnsFalse()
    {
        // Act
        var result = await _receiver.IsTransactionKnownAsync("unknown-hash");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTransactionKnownAsync_KnownHash_ReturnsTrue()
    {
        // Arrange
        const string hash = "known-hash";
        var data = CreateTransactionBytes("tx-1", hash);

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult { TransactionId = "tx-1", RegisterId = "register-1", IsValid = true });

        _memPoolManagerMock.Setup(x => x.AddTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _receiver.ReceiveTransactionAsync(hash, data, "peer-1");

        // Act
        var result = await _receiver.IsTransactionKnownAsync(hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTransactionKnownAsync_ExistsInMemPool_ReturnsTrue()
    {
        // Arrange - hash not in local cache, but exists in mempool
        const string hash = "mempool-hash";

        _memPoolManagerMock.Setup(x => x.TransactionExistsAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _receiver.IsTransactionKnownAsync(hash);

        // Assert
        result.Should().BeTrue();
        _memPoolManagerMock.Verify(x => x.TransactionExistsAsync(hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsTransactionKnownAsync_NotInCacheOrMemPool_ReturnsFalse()
    {
        // Arrange - hash not in local cache and not in mempool
        const string hash = "unknown-hash";

        _memPoolManagerMock.Setup(x => x.TransactionExistsAsync(hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _receiver.IsTransactionKnownAsync(hash);

        // Assert
        result.Should().BeFalse();
        _memPoolManagerMock.Verify(x => x.TransactionExistsAsync(hash, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _receiver.GetStats();

        // Assert
        stats.TotalReceived.Should().Be(0);
        stats.TotalAccepted.Should().Be(0);
        stats.TotalRejected.Should().Be(0);
        stats.TotalDuplicates.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_AfterOperations_TracksCorrectly()
    {
        // Arrange
        const string validHash = "valid-hash";
        var validData = CreateTransactionBytes("tx-1", validHash);
        var invalidData = Encoding.UTF8.GetBytes("invalid");

        _validationEngineMock.Setup(x => x.ValidateTransactionAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationEngineResult { TransactionId = "tx-1", RegisterId = "register-1", IsValid = true });

        _memPoolManagerMock.Setup(x => x.AddTransactionAsync(It.IsAny<string>(), It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _receiver.ReceiveTransactionAsync(validHash, validData, "peer-1"); // Accepted
        await _receiver.ReceiveTransactionAsync("bad", invalidData, "peer-2"); // Rejected (invalid)
        await _receiver.ReceiveTransactionAsync(validHash, validData, "peer-3"); // Duplicate

        var stats = _receiver.GetStats();

        // Assert
        stats.TotalReceived.Should().Be(3);
        stats.TotalAccepted.Should().Be(1);
        stats.TotalRejected.Should().Be(1);
        stats.TotalDuplicates.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateTransactionBytes(string transactionId, string? payloadHash = null)
    {
        var transaction = new Transaction
        {
            TransactionId = transactionId,
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = payloadHash ?? $"hash-{transactionId}",
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = TransactionPriority.Normal,
            Signatures = new List<Signature>
            {
                new()
                {
                    PublicKey = new byte[] { 1, 2, 3 },
                    SignatureValue = new byte[] { 4, 5, 6 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            Metadata = new Dictionary<string, string>()
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.SerializeToUtf8Bytes(transaction, options);
    }

    #endregion
}
