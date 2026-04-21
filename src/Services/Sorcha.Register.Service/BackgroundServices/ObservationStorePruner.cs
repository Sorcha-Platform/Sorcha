// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.Observations;

namespace Sorcha.Register.Service.BackgroundServices;

/// <summary>
/// Background pruner that evicts observation buckets for registers that have been silent
/// (no peer advert, no validator sealing push) for longer than <see cref="SilenceWindow"/>.
/// Prevents unbounded growth when registers are deleted or become inactive (Feature 108).
/// </summary>
public sealed class ObservationStorePruner : BackgroundService
{
    /// <summary>How long a register must be silent before its observation bucket is evicted.</summary>
    public static readonly TimeSpan SilenceWindow = TimeSpan.FromMinutes(30);

    /// <summary>How often the pruner sweeps.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IObservationStore _store;
    private readonly ILogger<ObservationStorePruner> _logger;

    public ObservationStorePruner(IObservationStore store, ILogger<ObservationStorePruner> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ObservationStorePruner started (silence window: {SilenceWindow}, sweep interval: {SweepInterval})",
            SilenceWindow, SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
                SweepOnce();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ObservationStorePruner sweep errored");
            }
        }

        _logger.LogInformation("ObservationStorePruner stopped");
    }

    internal void SweepOnce()
    {
        var tracked = _store.GetTrackedRegisterIds();
        if (tracked.Count == 0) return;

        var evicted = 0;
        foreach (var registerId in tracked)
        {
            var heights = _store.GetRecentPeerHeights(registerId, SilenceWindow);
            var sealing = _store.GetLatestValidatorSealing(registerId);

            var anyFresh = heights.Count > 0
                || (sealing is not null && sealing.ObservedAt >= DateTimeOffset.UtcNow - SilenceWindow);

            if (!anyFresh)
            {
                _store.Evict(registerId);
                evicted++;
            }
        }

        if (evicted > 0)
        {
            _logger.LogInformation(
                "ObservationStorePruner evicted {Count} silent register(s) from the observation store",
                evicted);
        }
    }
}
