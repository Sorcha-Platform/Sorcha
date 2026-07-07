// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 119 — GitHub issue #585 — real-Redis integration tests for
/// <see cref="RedisPresentationSealCoordinator"/> (scenarios T017-T021).
/// </summary>
/// <remarks>
/// These tests connect to the docker-compose Redis (host port 16379 by default,
/// overridable via the <c>REDIS_CONNECTION</c> env var) and SKIP cleanly when
/// Redis is unreachable, mirroring the graceful-degradation approach used by the
/// other Sorcha integration tests. Each test runs against a dedicated logical
/// database (<see cref="TestDb"/> = 15) which is flushed on construction and
/// disposal so the sweep's keyspace scan is deterministic and never touches the
/// real Sorcha data on DB 0. Predecessor txIds are GUID-namespaced per test to
/// avoid any cross-test collision.
///
/// Scoped collaborators the drain / sweep resolve via
/// <see cref="IServiceScopeFactory"/> and therefore mocked here:
/// <list type="bullet">
///   <item><see cref="IValidatorServiceClient"/> — submission drain</item>
///   <item><see cref="IRegisterServiceClient"/> — sweep seal-poll</item>
///   <item><see cref="IPendingPresentationStore"/> — sentinel writes</item>
///   <item><see cref="IActionExecutionService"/> — advancement drain</item>
/// </list>
/// </remarks>
public sealed class PresentationSealCoordinatorIntegrationTests : IDisposable
{
    private const int TestDb = 15;
    private const string SubmitPrefix = "sorcha:presentation:awaiting-seal:submit:";
    private const string AdvancePrefix = "sorcha:presentation:awaiting-seal:advance:";

    private readonly IConnectionMultiplexer? _redis;
    private readonly bool _redisAvailable;

    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly Mock<IRegisterServiceClient> _register = new();
    private readonly Mock<IPendingPresentationStore> _store = new();
    private readonly Mock<IActionExecutionService> _actionExecution = new();
    private readonly PresentationLifecycleMetrics _metrics;
    private readonly TestClock _clock = new();
    private readonly IServiceScopeFactory _scopeFactory;

    public PresentationSealCoordinatorIntegrationTests()
    {
        _metrics = new PresentationLifecycleMetrics(new TestMeterFactory());

        var services = new ServiceCollection();
        services.AddSingleton(_actionExecution.Object);
        services.AddScoped(_ => _validator.Object);
        services.AddScoped(_ => _register.Object);
        services.AddScoped(_ => _store.Object);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        _redis = TryConnect();
        _redisAvailable = _redis is not null && _redis.IsConnected;

        if (_redisAvailable)
        {
            var server = _redis!.GetServer(_redis.GetEndPoints()[0]);
            server.FlushDatabase(TestDb);
        }
    }

    private static IConnectionMultiplexer? TryConnect()
    {
        var candidates = new List<string>();
        var fromEnv = Environment.GetEnvironmentVariable("REDIS_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnv)) candidates.Add(fromEnv);
        candidates.Add("localhost:16379");
        candidates.Add("localhost:6379");

        foreach (var endpoint in candidates)
        {
            try
            {
                var config = ConfigurationOptions.Parse(endpoint);
                config.AbortOnConnectFail = false;
                config.ConnectTimeout = 2000;
                config.ConnectRetry = 1;
                config.AllowAdmin = true;      // required for FlushDatabase + KeysAsync scan
                config.DefaultDatabase = TestDb;
                var mux = ConnectionMultiplexer.Connect(config);
                if (mux.IsConnected)
                {
                    return mux;
                }
                mux.Dispose();
            }
            catch (RedisConnectionException) { /* try next candidate */ }
            catch (RedisException) { /* try next candidate */ }
        }
        return null;
    }

    private void RequireRedis()
    {
        if (!_redisAvailable)
        {
            Assert.Skip(
                "Redis not reachable on REDIS_CONNECTION / localhost:16379 / localhost:6379 — " +
                "integration test skipped (compiles + skip-guarded).");
        }
    }

    private RedisPresentationSealCoordinator MakeSut(IConnectionMultiplexer? redis = null) => new(
        redis ?? _redis!,
        _scopeFactory,
        _metrics,
        _clock,
        Options.Create(new PresentationLifecycleOptions { DefaultValidityWindowSeconds = 600 }),
        new Mock<ILogger<RedisPresentationSealCoordinator>>().Object);

    private static TransactionSubmission FakeSubmission(
        string txId = "tx-outcome",
        string predecessor = "tx-init",
        string registerId = "reg-1") => new()
        {
            TransactionId = txId,
            RegisterId = registerId,
            Payload = JsonDocument.Parse("{}").RootElement,
            PayloadHash = "h",
            Signatures = new List<SignatureInfo>(),
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = predecessor,
            SequenceNumber = 1
        };

    private bool KeyExists(string key)
        => _redis!.GetDatabase().KeyExists(key);

    // ── T017 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RoundTripViaRealRedis_EnqueueThenDrain_SubmitsOnce_AndClearsKey()
    {
        RequireRedis();

        var predecessor = $"tx-init-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission(txId: $"tx-out-{Guid.NewGuid():N}", predecessor: predecessor);

        _validator
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        var sut = MakeSut();
        await sut.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: predecessor,
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: submission,
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 60,
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        KeyExists(SubmitPrefix + predecessor).Should().BeTrue("the submission was just enqueued");

        var drained = await sut.DrainOnSealAsync(predecessor);

        drained.Should().Be(1);
        KeyExists(SubmitPrefix + predecessor).Should().BeFalse("a successful drain deletes the Redis key");
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.Is<TransactionSubmission>(s => s.TransactionId == submission.TransactionId),
            It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.SetOutcomeSentinelAsync(
            requestId, "success", 60, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── T018 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task MissedEventRecoveredBySweeper_PredecessorSealed_DrainsViaPoll()
    {
        RequireRedis();

        var predecessor = $"tx-init-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission(
            txId: $"tx-out-{Guid.NewGuid():N}",
            predecessor: predecessor,
            registerId: "reg-recover");

        // Predecessor IS sealed on the register, but the transaction:confirmed
        // event was missed — the sweeper must poll the register and drain.
        _register
            .Setup(r => r.GetTransactionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionModel());
        _validator
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        var sut = MakeSut();
        await sut.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: predecessor,
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: submission,
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 600,           // wide window so we hit missed-event, not TTL
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        // Advance the clock past the 30s missed-event threshold (but well under 600s TTL).
        _clock.Advance(TimeSpan.FromSeconds(31));

        var result = await sut.RunRecoverySweepAsync();

        result.RecoveredViaPoll.Should().Be(1);
        result.FailedAtTtl.Should().Be(0);
        _register.Verify(r => r.GetTransactionAsync(
            It.IsAny<string>(), predecessor, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
        KeyExists(SubmitPrefix + predecessor).Should().BeFalse("the recovered entry was drained and deleted");
    }

    // ── T019 (SC-119-005) ─────────────────────────────────────────────────────
    [Fact]
    public async Task NeverSealsTimesOutAfterValidityWindow_SweeperTtlFails()
    {
        RequireRedis();

        var predecessor = $"tx-init-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission(txId: $"tx-out-{Guid.NewGuid():N}", predecessor: predecessor);

        var sut = MakeSut();
        await sut.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: predecessor,
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: submission,
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 1,             // tight window — predecessor never seals
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        // Advance past the 1s validity window so the sweeper TTL-fails the entry.
        _clock.Advance(TimeSpan.FromSeconds(2));

        var result = await sut.RunRecoverySweepAsync();

        result.FailedAtTtl.Should().Be(1);
        result.RecoveredViaPoll.Should().Be(0);
        _store.Verify(s => s.SetOutcomeSentinelAsync(
            requestId, "failed-predecessor-not-sealed", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Never);
        KeyExists(SubmitPrefix + predecessor).Should().BeFalse("a TTL-failed entry is deleted");
    }

    // ── T020 (SC-119-007) ─────────────────────────────────────────────────────
    [Fact]
    public async Task RestartSafety_DrainsAfterReconnect()
    {
        RequireRedis();

        var predecessor = $"tx-init-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission(txId: $"tx-out-{Guid.NewGuid():N}", predecessor: predecessor);

        _validator
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        // Coordinator instance A enqueues.
        var coordinatorA = MakeSut();
        await coordinatorA.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: predecessor,
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: submission,
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 60,
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        // Simulate a process restart: a fresh multiplexer connection + a brand-new
        // coordinator instance with no in-memory state. The queue must survive
        // because it lives in Redis, not the coordinator's heap.
        using var restartedMux = TryConnect()
            ?? throw new InvalidOperationException("Redis was reachable at construction but not on reconnect.");
        var coordinatorB = MakeSut(restartedMux);

        var drained = await coordinatorB.DrainOnSealAsync(predecessor);

        drained.Should().Be(1, "the Redis-persisted queue entry is drainable after a restart");
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
        KeyExists(SubmitPrefix + predecessor).Should().BeFalse();
    }

    // ── T021 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ConcurrentDrainIsIdempotent_OnlyOneWinnerSubmits()
    {
        RequireRedis();

        var predecessor = $"tx-init-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission(txId: $"tx-out-{Guid.NewGuid():N}", predecessor: predecessor);

        _validator
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        var sut = MakeSut();
        await sut.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: predecessor,
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: submission,
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 60,
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        // Two concurrent drains for the same txId. The KeyDelete idempotency guard
        // means exactly one wins — the sum of returned counts must be exactly 1.
        var results = await Task.WhenAll(
            sut.DrainOnSealAsync(predecessor),
            sut.DrainOnSealAsync(predecessor));

        results.Sum().Should().Be(1, "the KeyDelete guard admits exactly one drainer");
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
        KeyExists(SubmitPrefix + predecessor).Should().BeFalse();
    }

    public void Dispose()
    {
        if (_redisAvailable && _redis is not null)
        {
            try
            {
                var server = _redis.GetServer(_redis.GetEndPoints()[0]);
                server.FlushDatabase(TestDb);
            }
            catch (RedisException) { /* best-effort cleanup */ }
            _redis.Dispose();
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = DateTimeOffset.Parse("2026-05-08T12:00:00Z");
        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options);
        public void Dispose() { }
    }
}
