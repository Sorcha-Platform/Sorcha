// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

extern alias PeerService;

using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using FluentAssertions;
using Grpc.Core;
using Moq;

using Sorcha.Peer.Service.GrpcServices;
using Sorcha.Peer.Service.Replication;

using PeerService::Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DocketSyncGrpcService"/>.
/// Covers latest docket queries, streaming ranges, max count limiting,
/// register-not-found handling, and gap-in-chain stopping behavior.
/// </summary>
public class DocketSyncGrpcServiceTests
{
    private readonly RegisterCache _registerCache;
    private readonly DocketSyncGrpcService _service;

    private const string TestRegisterId = "test-register-001";

    public DocketSyncGrpcServiceTests()
    {
        _registerCache = new RegisterCache(NullLogger<RegisterCache>.Instance);
        _service = new DocketSyncGrpcService(
            _registerCache,
            NullLogger<DocketSyncGrpcService>.Instance);
    }

    // ────────────────────────────────────────────────────
    // GetLatestDocketNumber
    // ────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestDocketNumber_RegisterExists_ReturnsLatestDocket()
    {
        // Arrange — populate cache with 5 dockets
        var entry = _registerCache.GetOrCreate(TestRegisterId);
        for (var i = 1; i <= 5; i++)
        {
            entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: i));
        }

        var request = new GetLatestDocketNumberRequest { RegisterId = TestRegisterId };

        // Act
        var response = await _service.GetLatestDocketNumber(request, CreateTestContext());

        // Assert
        response.LatestDocketNumber.Should().Be(5);
        response.NetworkAvailable.Should().BeTrue();
        response.SourcePeerId.Should().NotBeNullOrEmpty();
        response.QueriedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLatestDocketNumber_RegisterNotFound_ReturnsZero()
    {
        // Arrange — empty cache, no registers
        var request = new GetLatestDocketNumberRequest { RegisterId = "nonexistent-register" };

        // Act
        var response = await _service.GetLatestDocketNumber(request, CreateTestContext());

        // Assert
        response.LatestDocketNumber.Should().Be(0);
        response.NetworkAvailable.Should().BeTrue();
        response.SourcePeerId.Should().NotBeNullOrEmpty();
    }

    // ────────────────────────────────────────────────────
    // SyncDockets
    // ────────────────────────────────────────────────────

    [Fact]
    public async Task SyncDockets_ValidRange_StreamsDocketsInOrder()
    {
        // Arrange — cache with dockets 1-5, request from=2 (streams 3, 4, 5)
        var entry = _registerCache.GetOrCreate(TestRegisterId);
        for (var i = 1; i <= 5; i++)
        {
            entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: i));
        }

        var request = new SyncDocketsRequest
        {
            RegisterId = TestRegisterId,
            FromDocketNumber = 2,
            ToDocketNumber = 0 // 0 = to head
        };

        var (writer, writtenEntries) = CreateMockStreamWriter();

        // Act
        await _service.SyncDockets(request, writer, CreateTestContext());

        // Assert — dockets 3, 4, 5 (from+1 to head)
        writtenEntries.Should().HaveCount(3);
        writtenEntries[0].DocketNumber.Should().Be(3);
        writtenEntries[1].DocketNumber.Should().Be(4);
        writtenEntries[2].DocketNumber.Should().Be(5);

        // Verify chain integrity data is populated
        foreach (var entry2 in writtenEntries)
        {
            entry2.DocketHash.Should().NotBeNullOrEmpty();
            entry2.DocketData.Should().NotBeEmpty();
            entry2.SealedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task SyncDockets_EmptyRange_StreamsNothing()
    {
        // Arrange — from=5, to=5 means range is (5, 5] which is empty (from+1=6 > 5)
        var entry = _registerCache.GetOrCreate(TestRegisterId);
        for (var i = 1; i <= 5; i++)
        {
            entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: i));
        }

        var request = new SyncDocketsRequest
        {
            RegisterId = TestRegisterId,
            FromDocketNumber = 5,
            ToDocketNumber = 5
        };

        var (writer, writtenEntries) = CreateMockStreamWriter();

        // Act
        await _service.SyncDockets(request, writer, CreateTestContext());

        // Assert
        writtenEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncDockets_MaxCountLimits_StreamsOnlyMaxCount()
    {
        // Arrange — 5 dockets available, maxCount=2
        var entry = _registerCache.GetOrCreate(TestRegisterId);
        for (var i = 1; i <= 5; i++)
        {
            entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: i));
        }

        var request = new SyncDocketsRequest
        {
            RegisterId = TestRegisterId,
            FromDocketNumber = 0, // start from beginning
            ToDocketNumber = 0,   // to head
            MaxCount = 2
        };

        var (writer, writtenEntries) = CreateMockStreamWriter();

        // Act
        await _service.SyncDockets(request, writer, CreateTestContext());

        // Assert — only first 2 dockets streamed despite 5 being available
        writtenEntries.Should().HaveCount(2);
        writtenEntries[0].DocketNumber.Should().Be(1);
        writtenEntries[1].DocketNumber.Should().Be(2);
    }

    [Fact]
    public async Task SyncDockets_RegisterNotFound_StreamsNothing()
    {
        // Arrange — register not in cache
        var request = new SyncDocketsRequest
        {
            RegisterId = "nonexistent-register",
            FromDocketNumber = 0,
            ToDocketNumber = 0
        };

        var (writer, writtenEntries) = CreateMockStreamWriter();

        // Act
        await _service.SyncDockets(request, writer, CreateTestContext());

        // Assert
        writtenEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncDockets_MissingDocket_StopsStream()
    {
        // Arrange — cache has dockets 1, 2, 3, 5 but NOT 4
        var entry = _registerCache.GetOrCreate(TestRegisterId);
        entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: 1));
        entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: 2));
        entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: 3));
        entry.AddOrUpdateDocket(CreateDocket(TestRegisterId, version: 5)); // skip 4

        var request = new SyncDocketsRequest
        {
            RegisterId = TestRegisterId,
            FromDocketNumber = 0, // start from beginning
            ToDocketNumber = 0    // to head (version 5)
        };

        var (writer, writtenEntries) = CreateMockStreamWriter();

        // Act
        await _service.SyncDockets(request, writer, CreateTestContext());

        // Assert — should stop at docket 3 because docket 4 is missing
        writtenEntries.Should().HaveCount(3);
        writtenEntries[0].DocketNumber.Should().Be(1);
        writtenEntries[1].DocketNumber.Should().Be(2);
        writtenEntries[2].DocketNumber.Should().Be(3);
    }

    // ────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────

    private static CachedDocket CreateDocket(string registerId, long version)
    {
        return new CachedDocket
        {
            RegisterId = registerId,
            Version = version,
            DocketHash = $"hash-{version:D4}",
            PreviousHash = version > 1 ? $"hash-{version - 1:D4}" : null,
            Data = Encoding.UTF8.GetBytes($"docket-data-{version}"),
            TransactionIds = [$"tx-{version}-a", $"tx-{version}-b"],
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10 + version)
        };
    }

    private static ServerCallContext CreateTestContext(CancellationToken ct = default)
    {
        return new TestServerCallContext(ct);
    }

    private static (IServerStreamWriter<SyncDocketEntry> Writer, List<SyncDocketEntry> Written)
        CreateMockStreamWriter()
    {
        var writtenEntries = new List<SyncDocketEntry>();
        var mockWriter = new Mock<IServerStreamWriter<SyncDocketEntry>>();
        mockWriter
            .Setup(x => x.WriteAsync(It.IsAny<SyncDocketEntry>(), It.IsAny<CancellationToken>()))
            .Callback<SyncDocketEntry, CancellationToken>((entry, _) => writtenEntries.Add(entry))
            .Returns(Task.CompletedTask);
        return (mockWriter.Object, writtenEntries);
    }

    /// <summary>
    /// Minimal ServerCallContext implementation for unit testing gRPC services.
    /// Required because ServerCallContext.CancellationToken is non-virtual and cannot be mocked.
    /// </summary>
    private sealed class TestServerCallContext(CancellationToken cancellationToken = default) : ServerCallContext
    {
        protected override string MethodCore => "TestMethod";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test-peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => cancellationToken;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore =>
            new(string.Empty, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) => throw new NotImplementedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }
}
