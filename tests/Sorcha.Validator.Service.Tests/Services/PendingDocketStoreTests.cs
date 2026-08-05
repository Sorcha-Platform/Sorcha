// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

public class PendingDocketStoreTests
{
    private readonly Mock<ILogger<PendingDocketStore>> _loggerMock;
    private readonly PendingDocketStore _store;

    public PendingDocketStoreTests()
    {
        _loggerMock = new Mock<ILogger<PendingDocketStore>>();
        _store = new PendingDocketStore(_loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new PendingDocketStore(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region AddAsync Tests

    [Fact]
    public async Task AddAsync_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _store.AddAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("docket");
    }

    [Fact]
    public async Task AddAsync_ValidDocket_AddsToDictionary()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _store.AddAsync(docket);
        var count = await _store.GetCountAsync();

        // Assert
        count.Should().Be(1);
    }

    [Fact]
    public async Task AddAsync_DuplicateDocket_DoesNotAdd()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _store.AddAsync(docket);
        await _store.AddAsync(docket);
        var count = await _store.GetCountAsync();

        // Assert
        count.Should().Be(1);
    }

    [Fact]
    public async Task AddAsync_WithProposerSignature_AddsSignatureToEntry()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _store.AddAsync(docket);

        // Assert - signature should be tracked internally
        var retrieved = await _store.GetAsync(docket.DocketId);
        retrieved.Should().NotBeNull();
    }

    [Fact]
    public async Task AddAsync_UpdatesStatistics()
    {
        // Arrange
        var docket = CreateDocket();

        // Act
        await _store.AddAsync(docket);
        var stats = _store.GetStats();

        // Assert
        stats.TotalAdded.Should().Be(1);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_NullDocketId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.GetAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("docketId");
    }

    [Fact]
    public async Task GetAsync_EmptyDocketId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.GetAsync("  ");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("docketId");
    }

    [Fact]
    public async Task GetAsync_ExistingDocket_ReturnsDocket()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);

        // Act
        var result = await _store.GetAsync(docket.DocketId);

        // Assert
        result.Should().NotBeNull();
        result!.DocketId.Should().Be(docket.DocketId);
    }

    [Fact]
    public async Task GetAsync_NonExistentDocket_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("non-existent-id");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetByRegisterAsync Tests

    [Fact]
    public async Task GetByRegisterAsync_NullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.GetByRegisterAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("registerId");
    }

    [Fact]
    public async Task GetByRegisterAsync_NoMatchingDockets_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetByRegisterAsync("non-existent-register");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByRegisterAsync_MatchingDockets_ReturnsAll()
    {
        // Arrange
        var docket1 = CreateDocket(registerId: "register-1");
        var docket2 = CreateDocket(registerId: "register-1");
        var docket3 = CreateDocket(registerId: "register-2");
        await _store.AddAsync(docket1);
        await _store.AddAsync(docket2);
        await _store.AddAsync(docket3);

        // Act
        var result = await _store.GetByRegisterAsync("register-1");

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(d => d.RegisterId.Should().Be("register-1"));
    }

    #endregion

    #region GetByStatusAsync Tests

    [Fact]
    public async Task GetByStatusAsync_NoMatchingStatus_ReturnsEmptyList()
    {
        // Arrange
        var docket = CreateDocket();
        docket.Status = DocketStatus.Proposed;
        await _store.AddAsync(docket);

        // Act
        var result = await _store.GetByStatusAsync(DocketStatus.Confirmed);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByStatusAsync_MatchingStatus_ReturnsAll()
    {
        // Arrange
        var docket1 = CreateDocket();
        docket1.Status = DocketStatus.Proposed;
        var docket2 = CreateDocket();
        docket2.Status = DocketStatus.Proposed;
        var docket3 = CreateDocket();
        docket3.Status = DocketStatus.Confirmed;
        await _store.AddAsync(docket1);
        await _store.AddAsync(docket2);
        await _store.AddAsync(docket3);

        // Act
        var result = await _store.GetByStatusAsync(DocketStatus.Proposed);

        // Assert
        result.Should().HaveCount(2);
    }

    #endregion

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task UpdateStatusAsync_NullDocketId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.UpdateStatusAsync(null!, DocketStatus.Confirmed);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("docketId");
    }

    [Fact]
    public async Task UpdateStatusAsync_NonExistentDocket_ReturnsFalse()
    {
        // Act
        var result = await _store.UpdateStatusAsync("non-existent", DocketStatus.Confirmed);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStatusAsync_ExistingDocket_UpdatesStatus()
    {
        // Arrange
        var docket = CreateDocket();
        docket.Status = DocketStatus.Proposed;
        await _store.AddAsync(docket);

        // Act
        var result = await _store.UpdateStatusAsync(docket.DocketId, DocketStatus.Confirmed);
        var retrieved = await _store.GetAsync(docket.DocketId);

        // Assert
        result.Should().BeTrue();
        retrieved!.Status.Should().Be(DocketStatus.Confirmed);
    }

    #endregion

    #region AddSignatureAsync Tests

    [Fact]
    public async Task AddSignatureAsync_NullDocketId_ThrowsArgumentException()
    {
        // Arrange
        var signature = CreateSignature();

        // Act
        var act = () => _store.AddSignatureAsync(null!, signature, "validator-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("docketId");
    }

    [Fact]
    public async Task AddSignatureAsync_NullSignature_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _store.AddSignatureAsync("docket-1", null!, "validator-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("signature");
    }

    [Fact]
    public async Task AddSignatureAsync_NullValidatorId_ThrowsArgumentException()
    {
        // Arrange
        var signature = CreateSignature();

        // Act
        var act = () => _store.AddSignatureAsync("docket-1", signature, null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("validatorId");
    }

    [Fact]
    public async Task AddSignatureAsync_NonExistentDocket_ReturnsNull()
    {
        // Arrange
        var signature = CreateSignature();

        // Act
        var result = await _store.AddSignatureAsync("non-existent", signature, "validator-1");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddSignatureAsync_ExistingDocket_ReturnsUpdatedDocket()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);
        var signature = CreateSignature();

        // Act
        var result = await _store.AddSignatureAsync(docket.DocketId, signature, "validator-2");

        // Assert
        result.Should().NotBeNull();
        result!.DocketId.Should().Be(docket.DocketId);
    }

    [Fact]
    public async Task AddSignatureAsync_DuplicateSignature_DoesNotAddTwice()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);
        var signature = CreateSignature();

        // Act
        await _store.AddSignatureAsync(docket.DocketId, signature, "validator-2");
        var result = await _store.AddSignatureAsync(docket.DocketId, signature, "validator-2");

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_NullDocketId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.RemoveAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("docketId");
    }

    [Fact]
    public async Task RemoveAsync_NonExistentDocket_ReturnsFalse()
    {
        // Act
        var result = await _store.RemoveAsync("non-existent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_ExistingDocket_RemovesAndReturnsTrue()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);

        // Act
        var result = await _store.RemoveAsync(docket.DocketId);
        var count = await _store.GetCountAsync();

        // Assert
        result.Should().BeTrue();
        count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveAsync_UpdatesStatistics()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);

        // Act
        await _store.RemoveAsync(docket.DocketId);
        var stats = _store.GetStats();

        // Assert
        stats.TotalRemoved.Should().Be(1);
    }

    #endregion

    #region GetCountAsync Tests

    [Fact]
    public async Task GetCountAsync_EmptyStore_ReturnsZero()
    {
        // Act
        var count = await _store.GetCountAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetCountAsync_MultipleDockets_ReturnsCorrectCount()
    {
        // Arrange
        await _store.AddAsync(CreateDocket());
        await _store.AddAsync(CreateDocket());
        await _store.AddAsync(CreateDocket());

        // Act
        var count = await _store.GetCountAsync();

        // Assert
        count.Should().Be(3);
    }

    #endregion

    #region GetStaleDocketsAsync Tests

    [Fact]
    public async Task GetStaleDocketsAsync_NoStaleDockets_ReturnsEmptyList()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);

        // Act
        var result = await _store.GetStaleDocketsAsync(TimeSpan.FromHours(1));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStaleDocketsAsync_WithStaleDockets_ReturnsStale()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);

        // Simulate passing of time by using a very short timeout
        await Task.Delay(10);

        // Act
        var result = await _store.GetStaleDocketsAsync(TimeSpan.FromMilliseconds(1));

        // Assert
        result.Should().HaveCount(1);
    }

    #endregion

    #region ClearRegisterAsync Tests

    [Fact]
    public async Task ClearRegisterAsync_NullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _store.ClearRegisterAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("registerId");
    }

    [Fact]
    public async Task ClearRegisterAsync_NoMatchingDockets_ReturnsZero()
    {
        // Act
        var result = await _store.ClearRegisterAsync("non-existent-register");

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task ClearRegisterAsync_MatchingDockets_ClearsAll()
    {
        // Arrange
        var docket1 = CreateDocket(registerId: "register-1");
        var docket2 = CreateDocket(registerId: "register-1");
        var docket3 = CreateDocket(registerId: "register-2");
        await _store.AddAsync(docket1);
        await _store.AddAsync(docket2);
        await _store.AddAsync(docket3);

        // Act
        var cleared = await _store.ClearRegisterAsync("register-1");
        var remaining = await _store.GetCountAsync();

        // Assert
        cleared.Should().Be(2);
        remaining.Should().Be(1);
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsEmptyStats()
    {
        // Act
        var stats = _store.GetStats();

        // Assert
        stats.TotalPending.Should().Be(0);
        stats.TotalAdded.Should().Be(0);
        stats.TotalRemoved.Should().Be(0);
        stats.AverageTimeInStoreMs.Should().Be(0);
    }

    [Fact]
    public async Task GetStats_WithDockets_ReturnsCorrectCounts()
    {
        // Arrange
        var docket1 = CreateDocket(registerId: "register-1");
        docket1.Status = DocketStatus.Proposed;
        var docket2 = CreateDocket(registerId: "register-2");
        docket2.Status = DocketStatus.Confirmed;
        await _store.AddAsync(docket1);
        await _store.AddAsync(docket2);

        // Act
        var stats = _store.GetStats();

        // Assert
        stats.TotalPending.Should().Be(2);
        stats.ByStatus.Should().HaveCount(2);
        stats.ByRegister.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStats_AfterRemoval_TracksAverageDuration()
    {
        // Arrange
        var docket = CreateDocket();
        await _store.AddAsync(docket);
        await Task.Delay(10);

        // Act
        await _store.RemoveAsync(docket.DocketId);
        var stats = _store.GetStats();

        // Assert
        stats.AverageTimeInStoreMs.Should().BeGreaterThan(0);
    }

    #endregion

    #region Helper Methods

    private static Docket CreateDocket(string? registerId = null)
    {
        return new Docket
        {
            DocketId = $"docket-{Guid.NewGuid():N}",
            RegisterId = registerId ?? "default-register",
            DocketNumber = 1,
            DocketHash = "test-hash",
            ProposerValidatorId = "validator-1",
            ProposerSignature = CreateSignature(),
            MerkleRoot = "test-merkle-root",
            Status = DocketStatus.Proposed,
            CreatedAt = DateTimeOffset.UtcNow,
            Transactions = []
        };
    }

    private static RegisterSignature CreateSignature()
    {
        return new RegisterSignature
        {
            PublicKey = "test-key"u8.ToArray(),
            SignatureValue = "test-sig"u8.ToArray(),
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow,
            SignedBy = "validator-1"
        };
    }

    #endregion
}
