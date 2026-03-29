// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.ServiceClients.Peer;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that pushes register advertisements to the Peer Service on startup
/// and periodically reconciles advertisement state.
/// </summary>
public class AdvertisementResyncService : BackgroundService
{
    private readonly IPeerServiceClient _peerClient;
    private readonly IRegisterRepository _repository;
    private readonly ILogger<AdvertisementResyncService> _logger;
    private readonly TimeSpan _resyncInterval;

    public AdvertisementResyncService(
        IPeerServiceClient peerClient,
        IRegisterRepository repository,
        ILogger<AdvertisementResyncService> logger,
        IOptions<AdvertisementResyncOptions>? options = null)
    {
        _peerClient = peerClient ?? throw new ArgumentNullException(nameof(peerClient));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resyncInterval = options?.Value.ResyncInterval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial startup push with retry
        await PushAdvertisementsWithRetryAsync(stoppingToken);

        // Periodic reconciliation loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_resyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await PushAdvertisementsWithRetryAsync(stoppingToken);
        }
    }

    private async Task PushAdvertisementsWithRetryAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(60);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var request = await BuildBulkAdvertiseRequestAsync(cancellationToken);
                var response = await _peerClient.BulkAdvertiseAsync(request, cancellationToken);

                if (response != null)
                {
                    _logger.LogInformation(
                        "Advertisement resync completed: {Processed} processed, {Added} added, {Updated} updated, {Removed} removed",
                        response.Processed, response.Added, response.Updated, response.Removed);
                    return; // Success — exit retry loop
                }

                _logger.LogWarning("Peer Service returned null response for bulk advertise — will retry");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Peer Service unavailable during advertisement resync — retrying in {Delay}s",
                    delay.TotalSeconds);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error during advertisement resync — retrying in {Delay}s",
                    delay.TotalSeconds);
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, 60s max
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
        }
    }

    public async Task<BulkAdvertiseRequest> BuildBulkAdvertiseRequestAsync(CancellationToken cancellationToken)
    {
        var registers = await _repository.QueryRegistersAsync(
            r => r.Advertise,
            cancellationToken);

        var advertisements = registers.Select(r => new AdvertisementItem
        {
            RegisterId = r.Id,
            Name = r.Name,
            Description = r.Description,
            IsPublic = true,
            LatestVersion = r.Height,
            LatestDocketVersion = 0
        }).ToList();

        _logger.LogDebug(
            "Built bulk advertise request with {Count} advertisements (FullSync=true)",
            advertisements.Count);

        return new BulkAdvertiseRequest
        {
            Advertisements = advertisements,
            FullSync = true
        };
    }
}

/// <summary>
/// Configuration options for the advertisement resync background service.
/// </summary>
public class AdvertisementResyncOptions
{
    public TimeSpan ResyncInterval { get; set; } = TimeSpan.FromMinutes(5);
}
