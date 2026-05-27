// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Configuration;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Register.Core.Events;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 119 — background service that drives the
/// <see cref="IPresentationSealCoordinator"/> drain loop.
/// </summary>
/// <remarks>
/// Two responsibilities:
/// <list type="bullet">
///   <item>Subscribes to the existing <c>transaction:confirmed</c> Redis Streams
///   channel via <see cref="IEventSubscriber"/> and calls
///   <see cref="IPresentationSealCoordinator.DrainOnSealAsync(string, CancellationToken)"/>
///   on each event.</item>
///   <item>Runs a periodic recovery sweep every
///   <c>SealRecoverySweepIntervalSeconds</c> (default 5s) to cover missed events
///   and never-seals timeouts.</item>
/// </list>
/// </remarks>
public sealed class PresentationSealSubscriber : BackgroundService
{
    private readonly IEventSubscriber _subscriber;
    private readonly IPresentationSealCoordinator _coordinator;
    private readonly IOptions<PresentationLifecycleOptions> _options;
    private readonly ILogger<PresentationSealSubscriber> _logger;

    /// <summary>Constructor — DI-friendly.</summary>
    public PresentationSealSubscriber(
        IEventSubscriber subscriber,
        IPresentationSealCoordinator coordinator,
        IOptions<PresentationLifecycleOptions> options,
        ILogger<PresentationSealSubscriber> logger)
    {
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PresentationSealSubscriber starting — subscribing to {Channel}",
            RegisterEventChannels.TransactionConfirmed);

        try
        {
            await _subscriber.SubscribeAsync<TransactionConfirmedEvent>(
                RegisterEventChannels.TransactionConfirmed,
                async e =>
                {
                    if (string.IsNullOrEmpty(e.TransactionId)) return;
                    try
                    {
                        var drained = await _coordinator.DrainOnSealAsync(e.TransactionId, CancellationToken.None);
                        if (drained > 0)
                        {
                            _logger.LogDebug("Seal subscriber drained {Count} entries on tx {TxId}",
                                drained, e.TransactionId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Drain failed for sealed tx {TxId}", e.TransactionId);
                    }
                },
                stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PresentationSealSubscriber failed to subscribe — recovery sweep will still run");
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.SealRecoverySweepIntervalSeconds));
        _logger.LogInformation("PresentationSealSubscriber recovery sweep tick = {Interval}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await _coordinator.RunRecoverySweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery sweep tick failed");
            }
        }

        _logger.LogInformation("PresentationSealSubscriber stopping");
    }
}
