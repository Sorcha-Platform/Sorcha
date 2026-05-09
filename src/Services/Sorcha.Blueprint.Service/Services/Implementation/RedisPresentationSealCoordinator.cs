// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Infrastructure;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 119 — Redis-backed implementation of
/// <see cref="IPresentationSealCoordinator"/>. Holds queued submissions and
/// workflow advancements until the predecessor they reference is observed
/// sealed via the <c>transaction:confirmed</c> Redis Streams channel.
/// </summary>
/// <remarks>
/// Two Redis hashes keyed by predecessor txId, per data-model.md §1:
/// <list type="bullet">
///   <item><c>sorcha:presentation:awaiting-seal:submit:{predecessorTxId}</c></item>
///   <item><c>sorcha:presentation:awaiting-seal:advance:{outcomeTxId}</c></item>
/// </list>
/// Singleton lifetime; resolves <c>IActionExecutionService</c> via
/// <c>IServiceScopeFactory</c> on advancement drain (mirrors PR #583).
/// </remarks>
public sealed class RedisPresentationSealCoordinator : IPresentationSealCoordinator
{
    private static readonly ActivitySource ActivitySource = new("Sorcha.Blueprint.PresentationLifecycle");

    private const string SubmitPrefix = "sorcha:presentation:awaiting-seal:submit:";
    private const string AdvancePrefix = "sorcha:presentation:awaiting-seal:advance:";

    /// <summary>Entries older than this with no seal event are polled directly via the register client.</summary>
    private static readonly TimeSpan MissedEventThreshold = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PresentationLifecycleMetrics _metrics;
    private readonly IClock _clock;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<RedisPresentationSealCoordinator> _logger;

    /// <summary>Constructor — DI-friendly. Scoped collaborators
    /// (<c>IValidatorServiceClient</c>, <c>IRegisterServiceClient</c>,
    /// <c>IPendingPresentationStore</c>, <c>IActionExecutionService</c>)
    /// are resolved per-drain via <see cref="IServiceScopeFactory"/> to avoid
    /// captive-dependency violations of this singleton.</summary>
    public RedisPresentationSealCoordinator(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        PresentationLifecycleMetrics metrics,
        IClock clock,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<RedisPresentationSealCoordinator> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnqueueSubmissionAsync(
        SealAwaitingSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var db = _redis.GetDatabase();
        var key = SubmitKey(submission.PredecessorTxId);
        var ttl = TimeSpan.FromSeconds(submission.ValidityWindowSeconds);

        var fields = new HashEntry[]
        {
            new("presentationRequestId", submission.PresentationRequestId.ToString()),
            new("site", submission.Site.ToString()),
            new("submissionJson", JsonSerializer.Serialize(submission.Submission)),
            new("targetSentinelOnSuccess", submission.TargetSentinelOnSuccess),
            new("validityWindowSeconds", submission.ValidityWindowSeconds),
            new("enqueuedAt", submission.EnqueuedAt.ToString("o")),
            new("traceContext", submission.TraceContext ?? string.Empty),
        };

        // Pipeline HSET + EXPIRE for atomic-from-the-client durability.
        var batch = db.CreateBatch();
        var setTask = batch.HashSetAsync(key, fields);
        var expireTask = batch.KeyExpireAsync(key, ttl);
        batch.Execute();
        await Task.WhenAll(setTask, expireTask);

        _metrics.IncrementSealQueueDepth(SiteLabel(submission.Site));

        _logger.LogInformation(
            "Enqueued seal-awaiting submission for requestId {RequestId} site={Site} predecessor={PredecessorTxId} ttl={TtlSeconds}s",
            submission.PresentationRequestId, submission.Site, submission.PredecessorTxId, submission.ValidityWindowSeconds);
    }

    /// <inheritdoc />
    public async Task EnqueueAdvancementAsync(
        SealAwaitingAdvancement advancement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(advancement);

        var db = _redis.GetDatabase();
        var key = AdvanceKey(advancement.OutcomeTxId);
        var ttl = TimeSpan.FromSeconds(_options.Value.DefaultValidityWindowSeconds);

        var fields = new HashEntry[]
        {
            new("presentationRequestId", advancement.PresentationRequestId.ToString()),
            new("instanceId", advancement.InstanceId.ToString()),
            new("completedActionId", advancement.CompletedActionId),
            new("registerId", advancement.RegisterId),
            new("draftPayloadJson", advancement.DraftPayload is null
                ? string.Empty
                : JsonSerializer.Serialize(advancement.DraftPayload)),
            new("enqueuedAt", advancement.EnqueuedAt.ToString("o")),
            new("traceContext", advancement.TraceContext ?? string.Empty),
        };

        var batch = db.CreateBatch();
        var setTask = batch.HashSetAsync(key, fields);
        var expireTask = batch.KeyExpireAsync(key, ttl);
        batch.Execute();
        await Task.WhenAll(setTask, expireTask);

        _metrics.IncrementSealQueueDepth("advance");

        _logger.LogInformation(
            "Enqueued seal-awaiting advancement for requestId {RequestId} outcomeTxId={OutcomeTxId} instance={InstanceId} actionId={ActionId}",
            advancement.PresentationRequestId, advancement.OutcomeTxId, advancement.InstanceId, advancement.CompletedActionId);
    }

    /// <inheritdoc />
    public async Task<int> DrainOnSealAsync(
        string sealedTxId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sealedTxId);

        var drained = 0;
        drained += await TryDrainSubmissionAsync(sealedTxId, recoveredViaSweeper: false, cancellationToken);
        drained += await TryDrainAdvancementAsync(sealedTxId, recoveredViaSweeper: false, cancellationToken);
        return drained;
    }

    /// <inheritdoc />
    public async Task<SweepResult> RunRecoverySweepAsync(CancellationToken cancellationToken = default)
    {
        var recovered = 0;
        var failed = 0;

        try
        {
            var endpoints = _redis.GetEndPoints();
            if (endpoints.Length == 0)
            {
                return new SweepResult(0, 0);
            }
            var server = _redis.GetServer(endpoints[0]);
            var db = _redis.GetDatabase();
            var now = _clock.UtcNow;

            // Submit-queue sweep.
            await foreach (var key in server.KeysAsync(pattern: SubmitPrefix + "*", pageSize: 250)
                .WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var keyStr = key.ToString();
                var predecessor = keyStr.Substring(SubmitPrefix.Length);
                var enqueuedAtRaw = await db.HashGetAsync(key, "enqueuedAt");
                if (!enqueuedAtRaw.HasValue) continue;
                if (!DateTimeOffset.TryParse(enqueuedAtRaw!, out var enqueuedAt)) continue;
                var age = now - enqueuedAt;

                var validityRaw = (string?)await db.HashGetAsync(key, "validityWindowSeconds");
                var validity = (!string.IsNullOrEmpty(validityRaw) && int.TryParse(validityRaw, out var v))
                    ? v
                    : _options.Value.DefaultValidityWindowSeconds;

                if (age >= TimeSpan.FromSeconds(validity))
                {
                    // TTL fail.
                    if (await TryFailSubmissionAsync(key, predecessor, cancellationToken))
                    {
                        failed++;
                    }
                    continue;
                }

                if (age >= MissedEventThreshold)
                {
                    // Missed-event recovery — poll register.
                    var registerIdRaw = await ResolveRegisterIdForSubmissionAsync(key);
                    if (string.IsNullOrEmpty(registerIdRaw)) continue;
                    var sealed_ = await IsSealedAsync(registerIdRaw, predecessor, cancellationToken);
                    if (sealed_)
                    {
                        var n = await TryDrainSubmissionAsync(predecessor, recoveredViaSweeper: true, cancellationToken);
                        if (n > 0) recovered++;
                    }
                }
            }

            // Advance-queue sweep — same shape.
            await foreach (var key in server.KeysAsync(pattern: AdvancePrefix + "*", pageSize: 250)
                .WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var keyStr = key.ToString();
                var outcomeTxId = keyStr.Substring(AdvancePrefix.Length);
                var enqueuedAtRaw = await db.HashGetAsync(key, "enqueuedAt");
                if (!enqueuedAtRaw.HasValue) continue;
                if (!DateTimeOffset.TryParse(enqueuedAtRaw!, out var enqueuedAt)) continue;
                var age = now - enqueuedAt;

                var validity = _options.Value.DefaultValidityWindowSeconds;
                if (age >= TimeSpan.FromSeconds(validity))
                {
                    if (await TryFailAdvancementAsync(key, outcomeTxId, cancellationToken))
                    {
                        failed++;
                    }
                    continue;
                }

                if (age >= MissedEventThreshold)
                {
                    var registerIdRaw = (string?)await db.HashGetAsync(key, "registerId");
                    if (string.IsNullOrEmpty(registerIdRaw)) continue;
                    var sealed_ = await IsSealedAsync(registerIdRaw, outcomeTxId, cancellationToken);
                    if (sealed_)
                    {
                        var n = await TryDrainAdvancementAsync(outcomeTxId, recoveredViaSweeper: true, cancellationToken);
                        if (n > 0) recovered++;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery sweep failed");
        }

        if (recovered > 0 || failed > 0)
        {
            _logger.LogInformation(
                "Seal recovery sweep drained {Recovered} via poll, failed {Failed} at TTL",
                recovered, failed);
        }

        return new SweepResult(recovered, failed);
    }

    private async Task<int> TryDrainSubmissionAsync(string predecessorTxId, bool recoveredViaSweeper, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = SubmitKey(predecessorTxId);
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) return 0;

        // HDEL of the whole key — idempotent. If a concurrent process already deleted it, we get false and exit.
        var deleted = await db.KeyDeleteAsync(key);
        if (!deleted) return 0;

        var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
        if (!Guid.TryParse(map.GetValueOrDefault("presentationRequestId"), out var requestId))
        {
            _logger.LogError("Submit-queue entry for {PredecessorTxId} had unparseable presentationRequestId", predecessorTxId);
            return 0;
        }

        var siteStr = map.GetValueOrDefault("site", "Outcome");
        if (!Enum.TryParse<SealAwaitingSubmissionSite>(siteStr, ignoreCase: true, out var site))
        {
            site = SealAwaitingSubmissionSite.Outcome;
        }

        var siteLabel = SiteLabel(site);
        _metrics.DecrementSealQueueDepth(siteLabel);

        var targetSentinel = map.GetValueOrDefault("targetSentinelOnSuccess", "success");
        var validity = int.TryParse(map.GetValueOrDefault("validityWindowSeconds"), out var vw)
            ? vw
            : _options.Value.DefaultValidityWindowSeconds;
        var submissionJson = map.GetValueOrDefault("submissionJson");
        var enqueuedAtRaw = map.GetValueOrDefault("enqueuedAt");
        DateTimeOffset.TryParse(enqueuedAtRaw, out var enqueuedAt);

        if (string.IsNullOrEmpty(submissionJson))
        {
            _logger.LogError("Submit-queue entry for {PredecessorTxId} requestId {RequestId} had empty submission payload", predecessorTxId, requestId);
            await TrySetSentinelAsync(requestId, "failed-validator-reject", validity);
            return 0;
        }

        TransactionSubmission? submission;
        try
        {
            submission = JsonSerializer.Deserialize<TransactionSubmission>(submissionJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialise queued submission for requestId {RequestId} predecessor {PredecessorTxId}", requestId, predecessorTxId);
            await TrySetSentinelAsync(requestId, "failed-validator-reject", validity);
            return 0;
        }
        if (submission is null)
        {
            await TrySetSentinelAsync(requestId, "failed-validator-reject", validity);
            return 0;
        }

        using var activity = ActivitySource.StartActivity("presentation.seal-wait");
        activity?.SetTag("presentation.request_id", requestId.ToString());
        activity?.SetTag("predecessor.tx_id", predecessorTxId);
        activity?.SetTag("site", siteLabel);

        TransactionSubmissionResult result;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var validatorClient = scope.ServiceProvider.GetRequiredService<IValidatorServiceClient>();
            result = await validatorClient.SubmitTransactionAsync(submission, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Validator submission threw for queued {Site} requestId {RequestId} predecessor {PredecessorTxId}",
                site, requestId, predecessorTxId);
            await TrySetSentinelAsync(requestId, "failed-validator-reject", validity);
            return 0;
        }

        if (result.Success)
        {
            await TrySetSentinelAsync(requestId, targetSentinel, validity);
            if (enqueuedAt != default)
            {
                _metrics.RecordSealWait(siteLabel, (_clock.UtcNow - enqueuedAt).TotalSeconds);
            }
            if (recoveredViaSweeper)
            {
                _metrics.RecordSealRecoveredViaSweeper(siteLabel);
            }
            _logger.LogInformation(
                "Drained queued {Site} submission txId={TxId} requestId={RequestId} predecessor={PredecessorTxId} sentinel={Sentinel} sweeperRecovery={Sweeper}",
                site, submission.TransactionId, requestId, predecessorTxId, targetSentinel, recoveredViaSweeper);
            return 1;
        }

        // VAL_CHAIN_FORK is treated as "already sealed via another path" — dedupe silently.
        if (string.Equals(result.ErrorCode, "VAL_CHAIN_FORK", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Queued {Site} submission for requestId {RequestId} dedup-rejected by VAL_CHAIN_FORK — already sealed via another path",
                site, requestId);
            // Sentinel already at terminal value via the other path; do not stomp.
            return 1;
        }

        // Any other validator rejection — should-not-happen path.
        _logger.LogError(
            "Validator rejected queued {Site} submission txId={TxId} requestId={RequestId} predecessor={PredecessorTxId}: [{ErrorCode}] {ErrorMessage}",
            site, submission.TransactionId, requestId, predecessorTxId, result.ErrorCode, result.ErrorMessage);
        await TrySetSentinelAsync(requestId, "failed-validator-reject", validity);
        return 1;
    }

    private async Task<int> TryDrainAdvancementAsync(string outcomeTxId, bool recoveredViaSweeper, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = AdvanceKey(outcomeTxId);
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) return 0;

        var deleted = await db.KeyDeleteAsync(key);
        if (!deleted) return 0;

        _metrics.DecrementSealQueueDepth("advance");

        var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
        if (!Guid.TryParse(map.GetValueOrDefault("presentationRequestId"), out var requestId)) return 0;
        if (!Guid.TryParse(map.GetValueOrDefault("instanceId"), out var instanceId)) return 0;
        if (!int.TryParse(map.GetValueOrDefault("completedActionId"), out var completedActionId)) return 0;

        IReadOnlyDictionary<string, object>? draftPayload = null;
        var draftJson = map.GetValueOrDefault("draftPayloadJson");
        if (!string.IsNullOrEmpty(draftJson))
        {
            try
            {
                draftPayload = JsonSerializer.Deserialize<Dictionary<string, object>>(draftJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deserialise draftPayload for advancement requestId {RequestId} outcomeTxId {OutcomeTxId}; advancing without payload",
                    requestId, outcomeTxId);
            }
        }

        var enqueuedAtRaw = map.GetValueOrDefault("enqueuedAt");
        DateTimeOffset.TryParse(enqueuedAtRaw, out var enqueuedAt);

        using var activity = ActivitySource.StartActivity("presentation.seal-wait");
        activity?.SetTag("presentation.request_id", requestId.ToString());
        activity?.SetTag("outcome.tx_id", outcomeTxId);
        activity?.SetTag("site", "advance");

        // Fresh DI scope + CancellationToken.None — same lifetime contract as PR #583.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var actionExecution = scope.ServiceProvider.GetRequiredService<IActionExecutionService>();
            await actionExecution.CompleteAfterPresentationAsync(
                instanceId: instanceId.ToString(),
                completedActionId: completedActionId,
                outcomeTransactionId: outcomeTxId,
                draftPayload: draftPayload,
                cancellationToken: CancellationToken.None);

            if (enqueuedAt != default)
            {
                _metrics.RecordSealWait("advance", (_clock.UtcNow - enqueuedAt).TotalSeconds);
            }
            if (recoveredViaSweeper)
            {
                _metrics.RecordSealRecoveredViaSweeper("advance");
            }

            _logger.LogInformation(
                "Drained queued advancement outcomeTxId={OutcomeTxId} requestId={RequestId} instance={InstanceId} actionId={ActionId} sweeperRecovery={Sweeper}",
                outcomeTxId, requestId, instanceId, completedActionId, recoveredViaSweeper);
            return 1;
        }
        catch (Exception ex)
        {
            // Advancement failures are loggable, not propagatable — outcome is on the register.
            _logger.LogError(ex,
                "Advancement drain failed outcomeTxId={OutcomeTxId} requestId={RequestId} instance={InstanceId} actionId={ActionId} — outcome is on the register but the action did not advance",
                outcomeTxId, requestId, instanceId, completedActionId);
            return 1;
        }
    }

    private async Task<bool> TryFailSubmissionAsync(RedisKey key, string predecessor, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) return false;

        var deleted = await db.KeyDeleteAsync(key);
        if (!deleted) return false;

        var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
        if (!Guid.TryParse(map.GetValueOrDefault("presentationRequestId"), out var requestId)) return false;
        var siteStr = map.GetValueOrDefault("site", "Outcome");
        Enum.TryParse<SealAwaitingSubmissionSite>(siteStr, ignoreCase: true, out var site);
        var siteLabel = SiteLabel(site);
        var validity = int.TryParse(map.GetValueOrDefault("validityWindowSeconds"), out var vw)
            ? vw : _options.Value.DefaultValidityWindowSeconds;

        _metrics.DecrementSealQueueDepth(siteLabel);
        _metrics.RecordSealTimeout(siteLabel);

        await TrySetSentinelAsync(requestId, "failed-predecessor-not-sealed", validity);

        _logger.LogError(
            "Seal-wait timeout (TTL exceeded) — failing queued {Site} submission for requestId {RequestId} predecessor {PredecessorTxId} sentinel=failed-predecessor-not-sealed",
            site, requestId, predecessor);
        return true;
    }

    private async Task<bool> TryFailAdvancementAsync(RedisKey key, string outcomeTxId, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(key);
        if (entries.Length == 0) return false;

        var deleted = await db.KeyDeleteAsync(key);
        if (!deleted) return false;

        var map = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
        Guid.TryParse(map.GetValueOrDefault("presentationRequestId"), out var requestId);

        _metrics.DecrementSealQueueDepth("advance");
        _metrics.RecordSealTimeout("advance");

        _logger.LogError(
            "Seal-wait timeout (TTL exceeded) for advancement requestId={RequestId} outcomeTxId={OutcomeTxId} — outcome never sealed",
            requestId, outcomeTxId);
        return true;
    }

    private async Task<bool> IsSealedAsync(string registerId, string txId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
            var tx = await registerClient.GetTransactionAsync(registerId, txId, ct);
            return tx is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Register poll for {TxId} on {RegisterId} threw; treating as not-yet-sealed", txId, registerId);
            return false;
        }
    }

    private async Task<string?> ResolveRegisterIdForSubmissionAsync(RedisKey key)
    {
        // Submit envelope embeds RegisterId inside the serialised TransactionSubmission JSON.
        var db = _redis.GetDatabase();
        var json = (string?)await db.HashGetAsync(key, "submissionJson");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("registerId", out var rid) ||
                doc.RootElement.TryGetProperty("RegisterId", out rid))
            {
                return rid.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    private async Task TrySetSentinelAsync(Guid requestId, string value, int validity)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pendingStore = scope.ServiceProvider.GetRequiredService<IPendingPresentationStore>();
            await pendingStore.SetOutcomeSentinelAsync(requestId, value, validity, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set sentinel {Value} for requestId {RequestId}", value, requestId);
        }
    }

    private static string SubmitKey(string predecessorTxId) => SubmitPrefix + predecessorTxId;
    private static string AdvanceKey(string outcomeTxId) => AdvancePrefix + outcomeTxId;
    private static string SiteLabel(SealAwaitingSubmissionSite site) => site switch
    {
        SealAwaitingSubmissionSite.Outcome => "outcome",
        SealAwaitingSubmissionSite.Abandonment => "abandonment",
        _ => "outcome"
    };
}
