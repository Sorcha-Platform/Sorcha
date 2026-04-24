// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 111 US4 — background worker that scans the Redis pending-presentation
/// keyspace for entries near TTL expiry and dispatches them to
/// <see cref="IPresentationLifecycleService.HandleAbandonmentAsync"/>. HA-safe: a
/// SET NX leader lock ensures only one replica runs the sweep loop at a time.
/// </summary>
public sealed class AbandonmentSweeper : BackgroundService
{
    private const string LeaderLockKey = "sorcha:presentation:sweeper-lock";

    private readonly IServiceProvider _services;
    private readonly IConnectionMultiplexer _redis;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<AbandonmentSweeper> _logger;
    private readonly string _instanceId;

    public AbandonmentSweeper(
        IServiceProvider services,
        IConnectionMultiplexer redis,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<AbandonmentSweeper> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tickInterval = TimeSpan.FromSeconds(_options.Value.SweeperIntervalSeconds);
        var lockTtl = TimeSpan.FromSeconds(_options.Value.SweeperLeaderLockTtlSeconds);

        _logger.LogInformation(
            "AbandonmentSweeper started: tick={TickSec}s lockTtl={LockSec}s instance={Instance}",
            tickInterval.TotalSeconds, lockTtl.TotalSeconds, _instanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(lockTtl, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AbandonmentSweeper tick failed; continuing to next tick");
            }

            try
            {
                await Task.Delay(tickInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("AbandonmentSweeper stopping");
    }

    // Internal for unit tests — exercises the leader-lock + dispatch path
    // without having to stand up the full BackgroundService loop.
    internal async Task TickAsync(TimeSpan lockTtl, CancellationToken ct)
    {
        // Leader election via SET NX — only the replica that acquires the lock
        // runs the scan this tick. Lock TTL is longer than the tick interval so
        // the leader's lock survives across ticks.
        var db = _redis.GetDatabase();
        var acquired = await db.StringSetAsync(
            LeaderLockKey, _instanceId, lockTtl, When.NotExists);
        if (!acquired)
        {
            _logger.LogTrace("AbandonmentSweeper tick skipped — another instance holds the leader lock");
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IPendingPresentationStore>();
            var lifecycle = scope.ServiceProvider.GetRequiredService<IPresentationLifecycleService>();

            // Near-expiry window: anything within the next tick interval
            // (+buffer) will expire before we scan again. Catch those this tick.
            var nearExpiryWindow = TimeSpan.FromSeconds(
                _options.Value.SweeperIntervalSeconds * 2);
            var candidates = await store.ListPendingNearExpiryAsync(
                nearExpiryWindow, max: 500, ct);

            if (candidates.Count == 0)
            {
                _logger.LogDebug("AbandonmentSweeper tick: no candidates near expiry");
                return;
            }

            _logger.LogInformation(
                "AbandonmentSweeper tick: dispatching {Count} near-expiry presentations",
                candidates.Count);

            foreach (var requestId in candidates)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await lifecycle.HandleAbandonmentAsync(requestId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "AbandonmentSweeper dispatch failed for requestId {RequestId}; continuing",
                        requestId);
                }
            }
        }
        finally
        {
            // Release the lock early so the next tick (possibly on a different
            // replica) isn't blocked waiting for the TTL to expire.
            await db.KeyDeleteAsync(LeaderLockKey);
        }
    }
}
