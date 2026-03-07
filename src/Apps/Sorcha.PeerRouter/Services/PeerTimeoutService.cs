// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.PeerRouter.Models;

namespace Sorcha.PeerRouter.Services;

/// <summary>
/// Background service that periodically sweeps the routing table
/// for peers that have exceeded the configured timeout period.
/// </summary>
public sealed class PeerTimeoutService : BackgroundService
{
    private readonly RoutingTable _routingTable;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _sweepInterval;
    private readonly ILogger<PeerTimeoutService> _logger;

    public PeerTimeoutService(
        RoutingTable routingTable,
        RouterConfiguration config,
        ILogger<PeerTimeoutService> logger)
    {
        _routingTable = routingTable;
        _timeout = TimeSpan.FromSeconds(config.PeerTimeoutSeconds);
        _sweepInterval = TimeSpan.FromSeconds(Math.Max(config.PeerTimeoutSeconds / 3, 5));
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Peer timeout service started (timeout: {Timeout}s, sweep: {Sweep}s)",
            _timeout.TotalSeconds, _sweepInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_sweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var unhealthy = _routingTable.SweepUnhealthyPeers(_timeout);
            if (unhealthy.Count > 0)
            {
                _logger.LogInformation("Marked {Count} peer(s) as unhealthy: {Peers}",
                    unhealthy.Count,
                    string.Join(", ", unhealthy.Select(p => p.PeerId)));
            }
        }
    }
}
