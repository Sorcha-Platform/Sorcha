// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Peer.Service.Models;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Peer.Service.Tests.Replication;

public class DocketFinalizationServiceTests
{
    private readonly Mock<IRegisterServiceClient> _registerClientMock;
    private readonly ValidatorKeyCache _validatorKeyCache;
    private readonly RegisterCache _registerCache;
    private readonly DocketFinalizationService _service;

    public DocketFinalizationServiceTests()
    {
        _registerClientMock = new Mock<IRegisterServiceClient>();
        _validatorKeyCache = new ValidatorKeyCache(new Mock<ILogger<ValidatorKeyCache>>().Object);
        _registerCache = new RegisterCache(new Mock<ILogger<RegisterCache>>().Object);

        _service = new DocketFinalizationService(
            new Mock<ILogger<DocketFinalizationService>>().Object,
            _registerClientMock.Object,
            _validatorKeyCache,
            _registerCache);
    }

    [Fact]
    public async Task FinalizeAsync_ValidDocket_WritesToRegisterService()
    {
        // Arrange
        var registerId = "reg-finalize-1";
        var docket = CreateValidCachedDocket(registerId, version: 0, previousHash: null);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var record = await _service.FinalizeAsync(registerId, docket);

        // Assert
        record.Status.Should().Be(FinalizationStatus.Finalized);
        record.FinalizedAt.Should().NotBeNull();
        record.DocketNumber.Should().Be(0);
        record.RegisterId.Should().Be(registerId);
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinalizeAsync_RegisterServiceRejects_StatusIsRejected()
    {
        // Arrange
        var registerId = "reg-reject-1";
        var docket = CreateValidCachedDocket(registerId, version: 0, previousHash: null);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var record = await _service.FinalizeAsync(registerId, docket);

        // Assert
        record.Status.Should().Be(FinalizationStatus.Rejected);
        record.ErrorMessage.Should().Contain("refused");
    }

    [Fact]
    public async Task FinalizeAsync_ChainBreak_Rejected()
    {
        // Arrange: docket 1 with wrong PreviousHash
        var registerId = "reg-chain-break";

        // Add docket 0 to cache
        var docket0 = CreateValidCachedDocket(registerId, version: 0, previousHash: null);
        var cacheEntry = _registerCache.GetOrCreate(registerId);
        cacheEntry.AddOrUpdateDocket(docket0);

        // Create docket 1 with wrong PreviousHash
        var docket1 = CreateValidCachedDocket(registerId, version: 1, previousHash: "wrong-hash");

        // Act
        var record = await _service.FinalizeAsync(registerId, docket1);

        // Assert
        record.Status.Should().Be(FinalizationStatus.Rejected);
        record.ErrorMessage.Should().Contain("Chain integrity");
    }

    [Fact]
    public async Task FinalizeAsync_AlreadyFinalized_ReturnsExisting()
    {
        // Arrange
        var registerId = "reg-idempotent";
        var docket = CreateValidCachedDocket(registerId, version: 0, previousHash: null);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act — finalize twice
        var first = await _service.FinalizeAsync(registerId, docket);
        var second = await _service.FinalizeAsync(registerId, docket);

        // Assert
        first.Status.Should().Be(FinalizationStatus.Finalized);
        second.Status.Should().Be(FinalizationStatus.Finalized);
        // Should only write once (second call skipped)
        _registerClientMock.Verify(
            c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FinalizeAsync_RegisterServiceDown_StatusRejectedWithError()
    {
        // Arrange
        var registerId = "reg-down";
        var docket = CreateValidCachedDocket(registerId, version: 0, previousHash: null);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Act
        var record = await _service.FinalizeAsync(registerId, docket);

        // Assert
        record.Status.Should().Be(FinalizationStatus.Rejected);
        record.ErrorMessage.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task FinalizeAsync_DuplicateConflict_TreatedAsSuccess()
    {
        // Arrange
        var registerId = "reg-conflict";
        var docket = CreateValidCachedDocket(registerId, version: 0, previousHash: null);

        _registerClientMock
            .Setup(c => c.WriteDocketAsync(It.IsAny<DocketModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Conflict", null, System.Net.HttpStatusCode.Conflict));

        // Act
        var record = await _service.FinalizeAsync(registerId, docket);

        // Assert
        record.Status.Should().Be(FinalizationStatus.Finalized);
        record.FinalizedAt.Should().NotBeNull();
    }

    [Fact]
    public void GetRecord_NonExistent_ReturnsNull()
    {
        _service.GetRecord("no-reg", 0).Should().BeNull();
    }

    [Fact]
    public void RecordCount_ReflectsState()
    {
        _service.RecordCount.Should().Be(0);
    }

    /// <summary>
    /// Creates a CachedDocket with valid serialized docket data including ProposerSignature,
    /// MerkleRoot, and CreatedAt, with a correctly computed DocketHash.
    /// </summary>
    private static CachedDocket CreateValidCachedDocket(
        string registerId,
        long version,
        string? previousHash)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var merkleRoot = "abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234";

        // Compute expected hash using same algorithm as DocketHasher / DocketFinalizationService
        var hashInput = new
        {
            RegisterId = registerId,
            DocketNumber = version,
            PreviousHash = previousHash ?? string.Empty,
            MerkleRoot = merkleRoot,
            Timestamp = createdAt.ToUnixTimeMilliseconds()
        };

        var hashJson = JsonSerializer.Serialize(hashInput, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashJson));
        var docketHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Build docket data JSON with ProposerSignature
        var docketData = JsonSerializer.Serialize(new
        {
            DocketNumber = version,
            RegisterId = registerId,
            MerkleRoot = merkleRoot,
            CreatedAt = createdAt,
            ProposerValidatorId = "validator-1",
            ProposerSignature = new
            {
                PublicKey = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }),
                SignatureValue = Convert.ToBase64String(new byte[] { 10, 20, 30 }),
                Algorithm = "ED25519",
                SignedAt = createdAt
            }
        });

        return new CachedDocket
        {
            RegisterId = registerId,
            Version = version,
            Data = Encoding.UTF8.GetBytes(docketData),
            DocketHash = docketHash,
            PreviousHash = previousHash,
            TransactionIds = new List<string> { "tx-001", "tx-002" },
            CreatedAt = createdAt
        };
    }
}
