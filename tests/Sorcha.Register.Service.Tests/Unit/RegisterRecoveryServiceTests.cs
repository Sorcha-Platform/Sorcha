// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FluentAssertions;
using Google.Protobuf;
using Moq;
using Sorcha.Peer.Service.Protos;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services.Implementation;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.Register.Service.Tests.Helpers;
using Sorcha.ServiceClients.Grpc;
using StackExchange.Redis;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="RegisterRecoveryService"/>.
/// Verifies gap detection, streaming recovery, bloom filter batch notifications,
/// state transitions (Synced/Recovering/Stalled), and retry on peer failure.
/// </summary>
public class RegisterRecoveryServiceTests
{
    private const string RegisterId = "test-register-001";
    private const string TransactionId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";

    private readonly Mock<IReadOnlyRegisterRepository> _mockRepository;
    private readonly Mock<IDocketSyncClient> _mockDocketSyncClient;
    private readonly Mock<IInboundTransactionRouter> _mockTransactionRouter;
    private readonly Mock<IDatabase> _mockDb;
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<RegisterRecoveryService>> _mockLogger;
    private readonly RegisterRecoveryService _sut;

    public RegisterRecoveryServiceTests()
    {
        _mockRepository = new Mock<IReadOnlyRegisterRepository>();
        _mockDocketSyncClient = new Mock<IDocketSyncClient>();
        _mockTransactionRouter = new Mock<IInboundTransactionRouter>();
        _mockLogger = new Mock<ILogger<RegisterRecoveryService>>();

        _mockDb = new Mock<IDatabase>();
        _mockDb.Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        _mockDb.Setup(x => x.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockRedis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDb.Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recovery:EnableAutoRecovery"] = "true",
                ["Recovery:MaxDocketsPerBatch"] = "100",
                ["Recovery:RetryDelaySeconds"] = "1",
                ["Recovery:MaxRetries"] = "3",
                ["Recovery:HealthCheckStalenessSeconds"] = "10"
            })
            .Build();

        _sut = new RegisterRecoveryService(
            _mockRepository.Object,
            _mockDocketSyncClient.Object,
            _mockTransactionRouter.Object,
            new InboundRoutingMetrics(new TestMeterFactory()),
            _mockRedis.Object,
            _configuration,
            _mockLogger.Object);
    }

    #region Helpers

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.CompletedTask;
        }
    }

    private static Models.Register CreateRegister(uint height) => new()
    {
        Id = RegisterId,
        Name = "Test Register",
        Height = height,
        TenantId = "tenant-001"
    };

    private static GetLatestDocketNumberResponse CreateNetworkResponse(
        long latestDocketNumber, bool networkAvailable = true) => new()
    {
        LatestDocketNumber = latestDocketNumber,
        SourcePeerId = "peer-001",
        NetworkAvailable = networkAvailable
    };

    private static SyncDocketEntry CreateDocketEntry(
        long docketNumber,
        string docketHash,
        string previousDocketHash,
        byte[]? docketData = null) => new()
    {
        DocketNumber = docketNumber,
        DocketHash = docketHash,
        PreviousDocketHash = previousDocketHash,
        DocketData = docketData != null
            ? ByteString.CopyFrom(docketData)
            : ByteString.Empty,
        TransactionCount = docketData != null ? 1 : 0
    };

    private static byte[] SerializeTransactions(List<TransactionModel> transactions) =>
        JsonSerializer.SerializeToUtf8Bytes(transactions);

    private void SetupRegister(uint height)
    {
        _mockRepository
            .Setup(r => r.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRegister(height));
    }

    private void SetupNetworkHead(long latestDocket, bool networkAvailable = true)
    {
        _mockDocketSyncClient
            .Setup(c => c.GetLatestDocketNumberAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateNetworkResponse(latestDocket, networkAvailable));
    }

    private void SetupLocalDocket(ulong docketId, string hash)
    {
        _mockRepository
            .Setup(r => r.GetDocketAsync(RegisterId, docketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Docket
            {
                Id = docketId,
                RegisterId = RegisterId,
                Hash = hash,
                PreviousHash = "prev-hash"
            });
    }

    #endregion

    #region RecoverIfNeededAsync — No Gap

    [Fact]
    public async Task RecoverIfNeededAsync_NoGap_ReturnsFalseAndStaysSynced()
    {
        // Arrange — local Height=6 means latest docket is 5, network also at 5
        SetupRegister(height: 6);
        SetupNetworkHead(latestDocket: 5);

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeFalse("no gap means no recovery needed");

        // Verify state was written as Synced
        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Synced")),
                It.IsAny<CommandFlags>()),
            Times.Once);

        // SyncDocketsAsync should never be called
        _mockDocketSyncClient.Verify(
            c => c.SyncDocketsAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region RecoverIfNeededAsync — Gap Detected

    [Fact]
    public async Task RecoverIfNeededAsync_GapDetected_TriggersRecoveryAndReturnsTrue()
    {
        // Arrange — local Height=4 → latest docket=3, network at 5 → gap of 2
        SetupRegister(height: 4);
        SetupNetworkHead(latestDocket: 5);
        SetupLocalDocket(docketId: 3, hash: "hash-003");

        var docketEntries = new List<SyncDocketEntry>
        {
            CreateDocketEntry(4, "hash-004", "hash-003"),
            CreateDocketEntry(5, "hash-005", "hash-004")
        };

        _mockDocketSyncClient
            .Setup(c => c.SyncDocketsAsync(
                RegisterId, 3, 5, 100, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(docketEntries));

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeTrue("gap was detected and recovery was executed");

        // Verify state transitions: Recovering first, then Synced at the end
        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Recovering")),
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);

        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Synced")),
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region RecoverIfNeededAsync — Network Unavailable

    [Fact]
    public async Task RecoverIfNeededAsync_NetworkUnavailable_ReturnsFalse()
    {
        // Arrange
        SetupRegister(height: 4);
        SetupNetworkHead(latestDocket: 10, networkAvailable: false);

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeFalse("network unavailable means recovery cannot proceed");

        // SyncDocketsAsync should not be called
        _mockDocketSyncClient.Verify(
            c => c.SyncDocketsAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region RecoverIfNeededAsync — Register Not Found

    [Fact]
    public async Task RecoverIfNeededAsync_RegisterNotFound_ReturnsFalse()
    {
        // Arrange — repository returns null for register
        _mockRepository
            .Setup(r => r.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Models.Register?)null);

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeFalse("register not found means nothing to recover");

        // Should not query the network at all
        _mockDocketSyncClient.Verify(
            c => c.GetLatestDocketNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region RecoverIfNeededAsync — Chain Integrity Violation

    [Fact]
    public async Task RecoverIfNeededAsync_ChainIntegrityViolation_StallsRecovery()
    {
        // Arrange — local Height=4 → latest docket=3, network at 5
        SetupRegister(height: 4);
        SetupNetworkHead(latestDocket: 5);
        SetupLocalDocket(docketId: 3, hash: "hash-003");

        // Docket entry has wrong previous hash — chain integrity violation
        var docketEntries = new List<SyncDocketEntry>
        {
            CreateDocketEntry(4, "hash-004", "WRONG-PREVIOUS-HASH")
        };

        _mockDocketSyncClient
            .Setup(c => c.SyncDocketsAsync(
                RegisterId, 3, 5, 100, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(docketEntries));

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert — recovery still returns true (it was attempted), but state is Stalled
        result.Should().BeTrue("recovery was triggered even though it stalled");

        // Verify state was set to Stalled
        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Stalled") &&
                    entries.Any(e => e.Name == "LastError" && e.Value.ToString().Contains("Chain integrity violation"))),
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region RecoverIfNeededAsync — Max Retries Exceeded

    [Fact]
    public async Task RecoverIfNeededAsync_MaxRetriesExceeded_StallsRecovery()
    {
        // Arrange — local Height=4 → latest docket=3, network at 5
        SetupRegister(height: 4);
        SetupNetworkHead(latestDocket: 5);
        SetupLocalDocket(docketId: 3, hash: "hash-003");

        // SyncDocketsAsync throws on every call — will exhaust all 3 retries
        _mockDocketSyncClient
            .Setup(c => c.SyncDocketsAsync(
                RegisterId, 3, 5, 100, It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("Peer connection failed"));

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert — recovery was triggered, state should be Stalled after max retries
        result.Should().BeTrue("recovery was triggered even though it stalled after retries");

        // Verify SyncDocketsAsync was called 3 times (MaxRetries=3)
        _mockDocketSyncClient.Verify(
            c => c.SyncDocketsAsync(
                RegisterId, 3, 5, 100, It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Verify state was set to Stalled with "Max retries exceeded" error
        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Stalled") &&
                    entries.Any(e => e.Name == "LastError" && e.Value.ToString().Contains("Max retries exceeded"))),
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region RecoverIfNeededAsync — Batch Notification for Action Transactions

    [Fact]
    public async Task RecoverIfNeededAsync_DocketWithActionTransactions_SendsBatchNotification()
    {
        // Arrange — local Height=4 → latest docket=3, network at 4
        SetupRegister(height: 4);
        SetupNetworkHead(latestDocket: 4);
        SetupLocalDocket(docketId: 3, hash: "hash-003");

        // Create transaction data with action-type transaction
        var transactions = new List<TransactionModel>
        {
            new()
            {
                TxId = TransactionId,
                RegisterId = RegisterId,
                SenderWallet = "sender-wallet-001",
                RecipientsWallets = new[] { "local-addr-001" },
                MetaData = new TransactionMetaData
                {
                    TransactionType = TransactionType.Action,
                    BlueprintId = "bp-001"
                }
            }
        };

        var docketData = SerializeTransactions(transactions);
        var docketEntries = new List<SyncDocketEntry>
        {
            CreateDocketEntry(4, "hash-004", "hash-003", docketData)
        };

        _mockDocketSyncClient
            .Setup(c => c.SyncDocketsAsync(
                RegisterId, 3, 4, 100, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(docketEntries));

        // Transaction router returns 1 (one bloom filter match)
        _mockTransactionRouter
            .Setup(r => r.RouteTransactionAsync(
                RegisterId,
                TransactionId,
                TransactionType.Action,
                It.Is<IReadOnlyList<string>>(l => l.Contains("local-addr-001")),
                "sender-wallet-001",
                It.IsAny<TransactionMetaData>(),
                4,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeTrue("recovery was triggered and completed");

        // Verify the transaction router was called with correct parameters
        _mockTransactionRouter.Verify(
            r => r.RouteTransactionAsync(
                RegisterId,
                TransactionId,
                TransactionType.Action,
                It.Is<IReadOnlyList<string>>(l => l.Contains("local-addr-001")),
                "sender-wallet-001",
                It.IsAny<TransactionMetaData>(),
                4,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify recovery completed with Synced status
        _mockDb.Verify(
            db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "Status" && e.Value == "Synced") &&
                    entries.Any(e => e.Name == "DocketsProcessed" && e.Value == "1")),
                It.IsAny<CommandFlags>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region RecoverIfNeededAsync — Streaming Processes Dockets Sequentially

    [Fact]
    public async Task RecoverIfNeededAsync_StreamingMultipleDockets_ProcessesDocketsSequentially()
    {
        // Arrange — local Height=3 → latest docket=2, network at 5 → 3 dockets to recover
        SetupRegister(height: 3);
        SetupNetworkHead(latestDocket: 5);
        SetupLocalDocket(docketId: 2, hash: "hash-002");

        var processedOrder = new List<long>();
        var docketEntries = new List<SyncDocketEntry>
        {
            CreateDocketEntry(3, "hash-003", "hash-002"),
            CreateDocketEntry(4, "hash-004", "hash-003"),
            CreateDocketEntry(5, "hash-005", "hash-004")
        };

        _mockDocketSyncClient
            .Setup(c => c.SyncDocketsAsync(
                RegisterId, 2, 5, 100, It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(docketEntries));

        // Track the order of state updates to verify sequential processing
        _mockDb
            .Setup(db => db.HashSetAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.Is<HashEntry[]>(entries =>
                    entries.Any(e => e.Name == "LocalLatestDocket")),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>((_, entries, _) =>
            {
                var docketEntry = entries.FirstOrDefault(e => e.Name == "LocalLatestDocket");
                if (long.TryParse(docketEntry.Value.ToString(), out var docketNumber) && docketNumber > 0)
                {
                    processedOrder.Add(docketNumber);
                }
            })
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.RecoverIfNeededAsync(RegisterId);

        // Assert
        result.Should().BeTrue();

        // Verify dockets were processed in sequential order
        processedOrder.Should().ContainInOrder(3, 4, 5);
    }

    #endregion

    #region GetRecoveryStateAsync — No State

    [Fact]
    public async Task GetRecoveryStateAsync_NoState_ReturnsNull()
    {
        // Arrange — empty hash
        _mockDb
            .Setup(db => db.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        // Act
        var state = await _sut.GetRecoveryStateAsync(RegisterId);

        // Assert
        state.Should().BeNull("no state in Redis means null");
    }

    #endregion

    #region GetRecoveryStateAsync — With State

    [Fact]
    public async Task GetRecoveryStateAsync_WithState_ReturnsDeserializedState()
    {
        // Arrange — mock HashGetAllAsync with populated entries
        var entries = new HashEntry[]
        {
            new("Status", "Recovering"),
            new("LocalLatestDocket", "42"),
            new("NetworkHeadDocket", "100"),
            new("StartedAt", "2026-03-01T10:00:00.0000000+00:00"),
            new("LastProgressAt", "2026-03-01T10:05:00.0000000+00:00"),
            new("DocketsProcessed", "37"),
            new("ErrorCount", "2"),
            new("LastError", "Timeout on peer sync")
        };

        _mockDb
            .Setup(db => db.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().Contains(RegisterId)),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);

        // Act
        var state = await _sut.GetRecoveryStateAsync(RegisterId);

        // Assert
        state.Should().NotBeNull();
        state!.RegisterId.Should().Be(RegisterId);
        state.Status.Should().Be(RecoveryStatus.Recovering);
        state.LocalLatestDocket.Should().Be(42);
        state.NetworkHeadDocket.Should().Be(100);
        state.DocketsProcessed.Should().Be(37);
        state.ErrorCount.Should().Be(2);
        state.LastError.Should().Be("Timeout on peer sync");
        state.StartedAt.Should().Be(DateTimeOffset.Parse("2026-03-01T10:00:00.0000000+00:00"));
        state.LastProgressAt.Should().Be(DateTimeOffset.Parse("2026-03-01T10:05:00.0000000+00:00"));
    }

    #endregion
}
