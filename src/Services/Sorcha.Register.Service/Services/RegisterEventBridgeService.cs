// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Service.Hubs;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Background service that bridges register domain events to SignalR notifications.
/// Subscribes to all register event topics and broadcasts to appropriate SignalR groups.
/// </summary>
public class RegisterEventBridgeService : BackgroundService
{
    private readonly IEventSubscriber _subscriber;
    private readonly IHubContext<RegisterHub, IRegisterHubClient> _hubContext;
    private readonly ILogger<RegisterEventBridgeService> _logger;

    public RegisterEventBridgeService(
        IEventSubscriber subscriber,
        IHubContext<RegisterHub, IRegisterHubClient> hubContext,
        ILogger<RegisterEventBridgeService> logger)
    {
        _subscriber = subscriber ?? throw new ArgumentNullException(nameof(subscriber));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegisterEventBridgeService registering event subscriptions");

        await _subscriber.SubscribeAsync<RegisterCreatedEvent>(
            RegisterEventChannels.RegisterCreated,
            async e =>
            {
                _logger.LogDebug("Bridging RegisterCreated for {RegisterId} to group register:{GroupRegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .RegisterCreated(e.RegisterId, e.Name);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<RegisterDeletedEvent>(
            "register:deleted",
            async e =>
            {
                _logger.LogDebug("Bridging RegisterDeleted for {RegisterId} to group register:{GroupRegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .RegisterDeleted(e.RegisterId);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<RegisterStatusChangedEvent>(
            "register:status-changed",
            async e =>
            {
                _logger.LogDebug("Bridging RegisterStatusChanged for {RegisterId} to group register:{GroupRegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .RegisterStatusChanged(e.RegisterId, e.NewStatus);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<TransactionConfirmedEvent>(
            "transaction:confirmed",
            async e =>
            {
                _logger.LogDebug("Bridging TransactionConfirmed for {RegisterId} to register:{RegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .TransactionConfirmed(e.RegisterId, e.TransactionId);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<DocketConfirmedEvent>(
            "docket:confirmed",
            async e =>
            {
                _logger.LogDebug("Bridging DocketSealed for {RegisterId} to register:{RegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .DocketSealed(e.RegisterId, e.DocketId, e.Hash);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<RegisterHeightUpdatedEvent>(
            "register:height-updated",
            async e =>
            {
                _logger.LogDebug("Bridging RegisterHeightUpdated for {RegisterId} to register:{RegisterId}", e.RegisterId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .RegisterHeightUpdated(e.RegisterId, e.NewHeight);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<RegisterSyncStateChangedEvent>(
            "register:sync-state-changed",
            async e =>
            {
                _logger.LogDebug("Bridging RegisterSyncStateChanged for {RegisterId} to group {GroupName}", e.RegisterId, RegisterHubGroups.Register(e.RegisterId));
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .RegisterSyncStateChanged(e.RegisterId, e.SyncState);
            },
            stoppingToken);

        await _subscriber.SubscribeAsync<ReceiptGeneratedEvent>(
            "receipt:generated",
            async e =>
            {
                _logger.LogDebug("Bridging ReceiptGenerated for {RegisterId} docket {DocketNumber} tx {TransactionId} to group register:{GroupRegisterId}",
                    e.RegisterId, e.DocketNumber, e.TransactionId, e.RegisterId);
                await _hubContext.Clients
                    .Group(RegisterHubGroups.Register(e.RegisterId))
                    .TransactionReceipt(
                        e.TransactionId,
                        e.RegisterId,
                        e.DocketNumber,
                        e.ReceiptId,
                        e.SealedAt);
            },
            stoppingToken);

        _logger.LogInformation("RegisterEventBridgeService subscriptions registered");
    }
}
