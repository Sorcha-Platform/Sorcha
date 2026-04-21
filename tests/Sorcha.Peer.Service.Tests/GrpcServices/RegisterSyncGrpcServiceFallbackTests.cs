// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using FluentAssertions;
using Moq;

using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.GrpcServices;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Peer.Service.Tests.GrpcServices;

/// <summary>
/// Finding A fix — when a subscriber peer calls <c>PullDocketTransactions</c> on the
/// register owner, the owner's peer-service cache is empty for registers it owns
/// (cache is populated on replication, not on local docket seal). Without a repository
/// fallback the subscriber gets <c>NotFound</c> past genesis and sync stalls. These
/// tests pin the fallback behaviour so the regression doesn't return.
/// </summary>
public class RegisterSyncGrpcServiceFallbackTests
{
    private const string RegisterId = "reg-fallback-test";

    [Fact]
    public async Task PullDocketTransactions_CacheMiss_FallsBackToRegisterService()
    {
        var tx1 = BuildTx("tx-1", senderWallet: "ws11qalpha");
        var tx2 = BuildTx("tx-2", senderWallet: "ws11qbeta");
        var writer = new RecordingServerStreamWriter<TransactionEntry>();
        var sut = BuildSut(new Dictionary<string, TransactionModel>
        {
            [tx1.TxId] = tx1,
            [tx2.TxId] = tx2,
        });

        var request = new DocketTransactionRequest { RegisterId = RegisterId, PeerId = "peer-probe" };
        request.TransactionIds.AddRange(new[] { tx1.TxId, tx2.TxId });

        await sut.PullDocketTransactions(request, writer, new TestServerCallContext());

        writer.Entries.Should().HaveCount(2, "both requested transactions exist in Register Service");
        writer.Entries.Select(e => e.TransactionId).Should().BeEquivalentTo(new[] { "tx-1", "tx-2" });

        var first = writer.Entries.First(e => e.TransactionId == "tx-1");
        first.RegisterId.Should().Be(RegisterId);
        first.TransactionData.ToByteArray().Should().NotBeEmpty();
        first.Checksum.Should().NotBeNullOrEmpty("checksum is required for the subscriber's integrity check");

        // Checksum MUST be SHA-256 hex of the bytes we stream; otherwise the mismatch
        // guard in RegisterReplicationService drops every transaction.
        var localChecksum = Convert.ToHexString(SHA256.HashData(first.TransactionData.ToByteArray())).ToLowerInvariant();
        first.Checksum.Should().Be(localChecksum);
    }

    [Fact]
    public async Task PullDocketTransactions_CacheMiss_PerTxNotFound_SkipsSilently()
    {
        // Register exists in Register Service, but a requested tx id does not.
        // Matches the cache-hit "not found, skip" behaviour at lines 137-141 of the
        // service — we do NOT blow up the stream over a single missing id.
        var tx1 = BuildTx("tx-real", senderWallet: "ws11qreal");
        var writer = new RecordingServerStreamWriter<TransactionEntry>();
        var sut = BuildSut(new Dictionary<string, TransactionModel> { [tx1.TxId] = tx1 });

        var request = new DocketTransactionRequest { RegisterId = RegisterId, PeerId = "peer-probe" };
        request.TransactionIds.AddRange(new[] { tx1.TxId, "tx-missing" });

        await sut.PullDocketTransactions(request, writer, new TestServerCallContext());

        writer.Entries.Should().ContainSingle(e => e.TransactionId == "tx-real");
    }

    [Fact]
    public async Task PullDocketTransactions_RegisterUnknownEverywhere_DoesNotThrow()
    {
        // Register Service also returns null (register doesn't exist at all). The
        // fallback should short-circuit to an empty stream — not an RpcException —
        // so the subscriber just moves on to its next sync cycle.
        var client = new Mock<IRegisterServiceClient>();
        client.Setup(c => c.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Register.Models.Register?)null);
        client.Setup(c => c.GetTransactionAsync(RegisterId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        var writer = new RecordingServerStreamWriter<TransactionEntry>();
        var sut = BuildSut(client.Object);

        var request = new DocketTransactionRequest { RegisterId = RegisterId, PeerId = "peer-probe" };
        request.TransactionIds.Add("tx-anything");

        await sut.PullDocketTransactions(request, writer, new TestServerCallContext());

        writer.Entries.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RegisterSyncGrpcService BuildSut(IDictionary<string, TransactionModel> txById)
    {
        var client = new Mock<IRegisterServiceClient>();
        client.Setup(c => c.GetRegisterAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Register.Models.Register { Id = RegisterId, Name = "test" });

        foreach (var (id, tx) in txById)
        {
            client.Setup(c => c.GetTransactionAsync(RegisterId, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(tx);
        }
        client.Setup(c => c.GetTransactionAsync(
                RegisterId,
                It.Is<string>(s => !txById.ContainsKey(s)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionModel?)null);

        return BuildSut(client.Object);
    }

    private static RegisterSyncGrpcService BuildSut(IRegisterServiceClient registerClient)
    {
        var services = new ServiceCollection();
        services.AddSingleton(registerClient);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var cache = new RegisterCache(NullLogger<RegisterCache>.Instance);
        var config = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "test-peer",
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 10,
                MinHealthyPeers = 1,
                RefreshIntervalMinutes = 5
            }
        });

        // PullDocketTransactions does not touch the background service, but the SUT
        // ctor null-checks it — build a minimum-viable instance just to pass the
        // guard. Its loop never runs because we never call ExecuteAsync.
        var peerList = new PeerListManager(NullLogger<PeerListManager>.Instance, config);
        var loggerFactory = new NullLoggerFactory();
        var connectionPool = new PeerConnectionPool(
            NullLogger<PeerConnectionPool>.Instance,
            loggerFactory,
            peerList,
            config,
            new PeerServiceMetrics(),
            new PeerServiceActivitySource());
        var advertisementService = new RegisterAdvertisementService(
            NullLogger<RegisterAdvertisementService>.Instance,
            peerList);
        var replicationService = new RegisterReplicationService(
            NullLogger<RegisterReplicationService>.Instance,
            connectionPool,
            peerList,
            advertisementService,
            cache,
            config);
        var bg = new RegisterSyncBackgroundService(
            NullLogger<RegisterSyncBackgroundService>.Instance,
            replicationService,
            config,
            scopeFactory);

        return new RegisterSyncGrpcService(
            cache,
            bg,
            scopeFactory,
            NullLogger<RegisterSyncGrpcService>.Instance,
            config);
    }

    private static TransactionModel BuildTx(string txId, string senderWallet)
    {
        return new TransactionModel
        {
            TxId = txId,
            RegisterId = RegisterId,
            SenderWallet = senderWallet,
            PrevTxId = new string('0', 64),
            Signature = "deadbeef",
            Payloads = Array.Empty<PayloadModel>(),
            PayloadCount = 0,
            TimeStamp = DateTime.UtcNow,
        };
    }

    private sealed class RecordingServerStreamWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Entries { get; } = new();
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Entries.Add(message);
            return Task.CompletedTask;
        }

        // Explicit CancellationToken overload — the default interface method in gRPC
        // 2.36+ delegates to the single-arg version, but pinning the behaviour here
        // documents what the production code actually calls and keeps us honest if
        // gRPC's contract ever changes.
        public Task WriteAsync(T message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestServerCallContext : ServerCallContext
    {
        protected override string MethodCore => "/test/PullDocketTransactions";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
