// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 119 — T009 unit tests for <see cref="RedisPresentationSealCoordinator"/>.
/// </summary>
/// <remarks>
/// See <c>specs/119-presentation-seal-ordering/EXECUTION-DEVIATIONS.md</c> for
/// scope: this file covers obligations 1-5 (round-trip + idempotence + reject paths)
/// via narrow mocks. Obligations 6-8 (sweep, TTL fail, restart safety) are covered
/// by integration tests T017 / T021 / T029 against real Redis.
/// </remarks>
public class PresentationSealCoordinatorTests
{
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<IBatch> _batch = new();
    private readonly Mock<IValidatorServiceClient> _validator = new();
    private readonly Mock<IRegisterServiceClient> _register = new();
    private readonly Mock<IPendingPresentationStore> _store = new();
    private readonly PresentationLifecycleMetrics _metrics;
    private readonly TestClock _clock = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Mock<IActionExecutionService> _actionExecution = new();

    public PresentationSealCoordinatorTests()
    {
        _metrics = new PresentationLifecycleMetrics(
            new TestMeterFactory());

        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_db.Object);
        _redis.Setup(r => r.GetEndPoints(It.IsAny<bool>()))
            .Returns(Array.Empty<System.Net.EndPoint>());

        _db.Setup(d => d.CreateBatch(It.IsAny<object>())).Returns(_batch.Object);
        _batch.Setup(b => b.HashSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);
        _batch.Setup(b => b.KeyExpireAsync(
                It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(true));

        var services = new ServiceCollection();
        services.AddSingleton(_actionExecution.Object);
        _scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private RedisPresentationSealCoordinator MakeSut() => new(
        _redis.Object,
        _validator.Object,
        _register.Object,
        _scopeFactory,
        _store.Object,
        _metrics,
        _clock,
        Options.Create(new PresentationLifecycleOptions()),
        new Mock<ILogger<RedisPresentationSealCoordinator>>().Object);

    private static TransactionSubmission FakeSubmission(string txId = "tx-outcome", string predecessor = "tx-init") => new()
    {
        TransactionId = txId,
        RegisterId = "reg-1",
        Payload = JsonDocument.Parse("{}").RootElement,
        PayloadHash = "h",
        Signatures = new List<SignatureInfo>(),
        CreatedAt = DateTimeOffset.UtcNow,
        PreviousTransactionId = predecessor,
        SequenceNumber = 1
    };

    [Fact]
    public async Task EnqueueSubmissionAsync_PersistsViaBatchedHashSetAndExpire()
    {
        var sut = MakeSut();
        var requestId = Guid.NewGuid();

        await sut.EnqueueSubmissionAsync(new SealAwaitingSubmission(
            PresentationRequestId: requestId,
            PredecessorTxId: "tx-init",
            Site: SealAwaitingSubmissionSite.Outcome,
            Submission: FakeSubmission(),
            TargetSentinelOnSuccess: "success",
            ValidityWindowSeconds: 600,
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        _batch.Verify(b => b.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString() == "sorcha:presentation:awaiting-seal:submit:tx-init"),
            It.IsAny<HashEntry[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
        _batch.Verify(b => b.KeyExpireAsync(
            It.IsAny<RedisKey>(),
            It.Is<TimeSpan?>(t => t.HasValue && t.Value == TimeSpan.FromSeconds(600)),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task EnqueueAdvancementAsync_PersistsAdvanceQueueEntry()
    {
        var sut = MakeSut();
        var requestId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await sut.EnqueueAdvancementAsync(new SealAwaitingAdvancement(
            PresentationRequestId: requestId,
            OutcomeTxId: "tx-outcome",
            InstanceId: instanceId,
            CompletedActionId: 7,
            RegisterId: "reg-1",
            DraftPayload: new Dictionary<string, object> { ["k"] = "v" },
            EnqueuedAt: _clock.UtcNow,
            TraceContext: ""));

        _batch.Verify(b => b.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString() == "sorcha:presentation:awaiting-seal:advance:tx-outcome"),
            It.IsAny<HashEntry[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task DrainOnSealAsync_NoQueueEntries_ReturnsZero()
    {
        // Both submit and advance lookups return empty.
        _db.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());

        var sut = MakeSut();
        var n = await sut.DrainOnSealAsync("tx-not-queued");
        n.Should().Be(0);
    }

    [Fact]
    public async Task DrainOnSealAsync_SubmitQueueHit_SubmitsToValidator_AndSetsTargetSentinel()
    {
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission();
        var entries = new HashEntry[]
        {
            new("presentationRequestId", requestId.ToString()),
            new("site", "Outcome"),
            new("submissionJson", JsonSerializer.Serialize(submission)),
            new("targetSentinelOnSuccess", "success"),
            new("validityWindowSeconds", 600),
            new("enqueuedAt", _clock.UtcNow.ToString("o")),
            new("traceContext", "")
        };

        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:submit:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:advance:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _validator.Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        var sut = MakeSut();
        var n = await sut.DrainOnSealAsync("tx-init");

        n.Should().Be(1);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.Is<TransactionSubmission>(s => s.TransactionId == submission.TransactionId),
            It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.SetOutcomeSentinelAsync(
            requestId, "success", 600, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DrainOnSealAsync_DuplicateEvent_IsIdempotent_NoSecondSubmission()
    {
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission();
        var entries = new HashEntry[]
        {
            new("presentationRequestId", requestId.ToString()),
            new("site", "Outcome"),
            new("submissionJson", JsonSerializer.Serialize(submission)),
            new("targetSentinelOnSuccess", "success"),
            new("validityWindowSeconds", 600),
            new("enqueuedAt", _clock.UtcNow.ToString("o")),
            new("traceContext", "")
        };

        var firstCall = true;
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:submit:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => firstCall ? entries : Array.Empty<HashEntry>());
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:advance:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => { var was = firstCall; firstCall = false; return was; });
        _validator.Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true, TransactionId = submission.TransactionId });

        var sut = MakeSut();
        var first = await sut.DrainOnSealAsync("tx-init");
        var second = await sut.DrainOnSealAsync("tx-init");

        first.Should().Be(1);
        second.Should().Be(0);
        _validator.Verify(v => v.SubmitTransactionAsync(
            It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DrainOnSealAsync_ValidatorRejectsWithChainFork_DedupesSilently_NoSentinelStomp()
    {
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission();
        var entries = new HashEntry[]
        {
            new("presentationRequestId", requestId.ToString()),
            new("site", "Outcome"),
            new("submissionJson", JsonSerializer.Serialize(submission)),
            new("targetSentinelOnSuccess", "success"),
            new("validityWindowSeconds", 600),
            new("enqueuedAt", _clock.UtcNow.ToString("o")),
            new("traceContext", "")
        };

        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:submit:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:advance:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _validator.Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorCode = "VAL_CHAIN_FORK",
                ErrorMessage = "fork detected"
            });

        var sut = MakeSut();
        var n = await sut.DrainOnSealAsync("tx-init");

        n.Should().Be(1);
        _store.Verify(s => s.SetOutcomeSentinelAsync(
            requestId, "failed-validator-reject", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DrainOnSealAsync_ValidatorRejectsOther_TransitionsToFailedValidatorReject()
    {
        var requestId = Guid.NewGuid();
        var submission = FakeSubmission();
        var entries = new HashEntry[]
        {
            new("presentationRequestId", requestId.ToString()),
            new("site", "Outcome"),
            new("submissionJson", JsonSerializer.Serialize(submission)),
            new("targetSentinelOnSuccess", "success"),
            new("validityWindowSeconds", 600),
            new("enqueuedAt", _clock.UtcNow.ToString("o")),
            new("traceContext", "")
        };

        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:submit:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:advance:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _validator.Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionSubmissionResult
            {
                Success = false,
                ErrorCode = "VAL_OTHER",
                ErrorMessage = "something else"
            });

        var sut = MakeSut();
        var n = await sut.DrainOnSealAsync("tx-init");

        n.Should().Be(1);
        _store.Verify(s => s.SetOutcomeSentinelAsync(
            requestId, "failed-validator-reject", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DrainOnSealAsync_AdvanceQueueHit_InvokesCompleteAfterPresentationAsync()
    {
        var requestId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var entries = new HashEntry[]
        {
            new("presentationRequestId", requestId.ToString()),
            new("instanceId", instanceId.ToString()),
            new("completedActionId", 5),
            new("registerId", "reg-1"),
            new("draftPayloadJson", "{\"k\":\"v\"}"),
            new("enqueuedAt", _clock.UtcNow.ToString("o")),
            new("traceContext", "")
        };

        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:submit:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<HashEntry>());
        _db.Setup(d => d.HashGetAllAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("sorcha:presentation:awaiting-seal:advance:")),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(entries);
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _actionExecution.Setup(a => a.CompleteAfterPresentationAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = MakeSut();
        var n = await sut.DrainOnSealAsync("tx-outcome");

        n.Should().Be(1);
        _actionExecution.Verify(a => a.CompleteAfterPresentationAsync(
            instanceId.ToString(), 5, "tx-outcome",
            It.IsAny<IReadOnlyDictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-05-08T12:00:00Z");
    }

    private sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
            new(options);
        public void Dispose() { }
    }
}
