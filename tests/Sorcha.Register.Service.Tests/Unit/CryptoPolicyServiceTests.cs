// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

public class CryptoPolicyServiceTests
{
    private readonly Mock<IRegisterRepository> _repositoryMock;
    private readonly TransactionManager _transactionManager;
    private readonly CryptoPolicyService _sut;

    public CryptoPolicyServiceTests()
    {
        _repositoryMock = new Mock<IRegisterRepository>();
        _transactionManager = new TransactionManager(
            _repositoryMock.Object,
            Mock.Of<IEventPublisher>());
        _sut = new CryptoPolicyService(
            _transactionManager,
            Mock.Of<ILogger<CryptoPolicyService>>());
    }

    [Fact]
    public async Task GetActivePolicyAsync_NoPolicySet_ReturnsDefault()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransactionModel>().AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(1);
        result.AcceptedSignatureAlgorithms.Should().NotBeEmpty();
        result.EnforcementMode.Should().Be(CryptoPolicyEnforcementMode.Permissive);
    }

    [Fact]
    public async Task GetActivePolicyAsync_GenesisHasPolicy_ReturnsGenesisPolicy()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        var policy = new CryptoPolicy
        {
            Version = 2,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65" },
            RequiredSignatureAlgorithms = new[] { "ED25519" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var controlRecord = new RegisterControlRecord
        {
            RegisterId = registerId,
            Name = "Test",
            TenantId = "tenant-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CryptoPolicy = policy,
            Attestations = new List<RegisterAttestation>()
        };

        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(controlRecord)));

        var genesisTx = CreateControlTx(registerId, payload, DateTime.UtcNow.AddDays(-1));

        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { genesisTx }.AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(2);
        result.EnforcementMode.Should().Be(CryptoPolicyEnforcementMode.Strict);
        result.AcceptedSignatureAlgorithms.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public async Task GetActivePolicyAsync_PolicyUpdateExists_ReturnsLatestUpdate()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";

        var updatePolicy = new CryptoPolicy
        {
            Version = 3,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65", "SLH-DSA-128s" },
            RequiredSignatureAlgorithms = new[] { "ML-DSA-65" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var updateTx = CreatePolicyUpdateTx(registerId, updatePolicy, DateTime.UtcNow);

        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { updateTx }.AsQueryable());

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(3);
        result.AcceptedSignatureAlgorithms.Should().HaveCount(3);
        result.RequiredSignatureAlgorithms.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public async Task GetPolicyHistoryAsync_MultiplePolicies_ReturnsOrderedByVersion()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";

        var policy1 = new CryptoPolicy
        {
            Version = 1,
            AcceptedSignatureAlgorithms = new[] { "ED25519" },
            EnforcementMode = CryptoPolicyEnforcementMode.Permissive
        };
        var policy2 = new CryptoPolicy
        {
            Version = 2,
            AcceptedSignatureAlgorithms = new[] { "ED25519", "ML-DSA-65" },
            EnforcementMode = CryptoPolicyEnforcementMode.Strict
        };

        var tx1 = CreatePolicyUpdateTx(registerId, policy1, DateTime.UtcNow.AddDays(-2));
        var tx2 = CreatePolicyUpdateTx(registerId, policy2, DateTime.UtcNow.AddDays(-1));

        // GetPolicyHistoryAsync calls GetTransactionsAsync multiple times
        // Return fresh queryable each call
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<IQueryable<TransactionModel>>(new[] { tx1, tx2 }.AsQueryable()));

        // Act
        var result = await _sut.GetPolicyHistoryAsync(registerId);

        // Assert — both are updates (not genesis), so all come from FindAllPolicyUpdatesAsync
        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.First().Version.Should().Be(1);
        result.Last().Version.Should().Be(2);
    }

    [Fact]
    public async Task GetActivePolicyAsync_ExceptionThrown_ReturnsDefault()
    {
        // Arrange
        var registerId = "abc123def456abc123def456abc123de";
        _repositoryMock
            .Setup(x => x.GetTransactionsAsync(registerId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB failure"));

        // Act
        var result = await _sut.GetActivePolicyAsync(registerId);

        // Assert
        result.Should().NotBeNull();
        result.Version.Should().Be(1); // Default
    }

    private static TransactionModel CreateControlTx(
        string registerId, string payloadData, DateTime timestamp)
    {
        return new TransactionModel
        {
            TxId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            SenderWallet = "system",
            Signature = "test",
            PayloadCount = 1,
            TimeStamp = timestamp,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payloadData,
                    Hash = "test",
                    WalletAccess = Array.Empty<string>()
                }
            },
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control
            }
        };
    }

    private static TransactionModel CreatePolicyUpdateTx(
        string registerId, CryptoPolicy policy, DateTime timestamp)
    {
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(policy)));

        return new TransactionModel
        {
            TxId = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            RegisterId = registerId,
            SenderWallet = "system",
            Signature = "test",
            PayloadCount = 1,
            TimeStamp = timestamp,
            Payloads = new[]
            {
                new PayloadModel
                {
                    Data = payload,
                    Hash = "test",
                    WalletAccess = Array.Empty<string>()
                }
            },
            MetaData = new TransactionMetaData
            {
                RegisterId = registerId,
                TransactionType = TransactionType.Control,
                TrackingData = new Dictionary<string, string>
                {
                    ["transactionType"] = "CryptoPolicyUpdate"
                }
            }
        };
    }
}
