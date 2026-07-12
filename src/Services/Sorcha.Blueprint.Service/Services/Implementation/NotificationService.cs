// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.ServiceDefaults.Hubs;
using StackExchange.Redis;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for broadcasting thin signal notifications via SignalR.
/// All signals are delivered exclusively through wallet-scoped groups.
/// Clients pull detailed data through authenticated REST endpoints.
/// </summary>
/// <remarks>
/// Encryption events are published to the <see cref="EncryptionEventEnvelope.ChannelName"/>
/// Redis channel rather than emitted directly on BlueprintHub. The
/// encryption pipeline lives in Blueprint Service, but the wire-level home
/// is WalletHub (in Wallet Service); the cross-service Redis bridge keeps
/// emission and hub-host in the same process per FR-006.
/// </remarks>
public class NotificationService : INotificationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHubContext<BlueprintHub, IBlueprintHubClient> _hubContext;
    private readonly IBlueprintInboxWriter _inboxWriter;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Initialises a new instance of the <see cref="NotificationService"/> class.</summary>
    public NotificationService(
        IHubContext<BlueprintHub, IBlueprintHubClient> hubContext,
        IBlueprintInboxWriter inboxWriter,
        IConnectionMultiplexer redis,
        ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _inboxWriter = inboxWriter;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyActionAvailableAsync(
        string instanceId, string? walletAddress, string? actionId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(walletAddress))
        {
            _logger.LogWarning(
                "Cannot send action-available signal for instance {InstanceId}: no wallet address linked",
                instanceId);
            return;
        }

        var resolvedActionId = actionId ?? string.Empty;
        var occurredAt = DateTimeOffset.UtcNow;
        var traceId = CurrentTraceId();
        var groupName = BlueprintHubGroups.Wallet(walletAddress);

        await _hubContext.Clients.Group(groupName)
            .ActionAvailable(instanceId, resolvedActionId, occurredAt, traceId);

        // Phase 5 follow-up of Feature 118: also drop a durable inbox entry. The writer
        // resolves wallet -> participant -> platform user and POSTs to Tenant. Failures
        // are swallowed so an inbox-write outage cannot break SignalR delivery.
        await _inboxWriter.WriteActionAvailableAsync(
            walletAddress: walletAddress,
            instanceId: instanceId,
            actionId: !string.IsNullOrEmpty(resolvedActionId) ? resolvedActionId : instanceId,
            actionTitle: null,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Sent ActionAvailable signal to wallet {Wallet} for instance {InstanceId}",
            walletAddress, instanceId);
    }

    /// <inheritdoc />
    public Task NotifyDecisionAsync(
        string recipientWalletAddress,
        string instanceId,
        string actionId,
        string title,
        string reason,
        string severity = "Warning",
        CancellationToken ct = default)
    {
        // Feature 183 (US2) — durable-only notice (no thin SignalR signal): the reject-visibility
        // requirement is the persistent bell/inbox entry carrying the reason. The writer resolves
        // wallet -> participant -> platform user and is itself fail-safe.
        return _inboxWriter.WriteDecisionAsync(
            recipientWalletAddress, instanceId, actionId, title, reason, severity, ct);
    }

    /// <inheritdoc />
    public async Task NotifyActionRejectedAsync(
        string instanceId, string? walletAddress, string? actionId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(walletAddress))
        {
            _logger.LogWarning(
                "Cannot send action-rejected signal for instance {InstanceId}: no wallet address linked",
                instanceId);
            return;
        }

        var resolvedActionId = actionId ?? string.Empty;
        var groupName = BlueprintHubGroups.Wallet(walletAddress);
        await _hubContext.Clients.Group(groupName)
            .ActionRejected(instanceId, resolvedActionId, DateTimeOffset.UtcNow, CurrentTraceId());

        _logger.LogInformation(
            "Sent ActionRejected signal to wallet {Wallet} for instance {InstanceId}",
            walletAddress, instanceId);
    }

    /// <inheritdoc />
    public async Task NotifyWorkflowCompletedAsync(
        string instanceId, IEnumerable<string> participantWalletAddresses, CancellationToken ct = default)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var traceId = CurrentTraceId();

        foreach (var walletAddress in participantWalletAddresses)
        {
            if (string.IsNullOrEmpty(walletAddress))
                continue;

            try
            {
                var groupName = BlueprintHubGroups.Wallet(walletAddress);
                await _hubContext.Clients.Group(groupName)
                    .WorkflowCompleted(instanceId, occurredAt, traceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send workflow-completed signal to wallet {Wallet} for instance {InstanceId}",
                    walletAddress, instanceId);
            }
        }

        _logger.LogInformation("Sent WorkflowCompleted signal for instance {InstanceId}", instanceId);
    }

    /// <inheritdoc />
    public Task NotifyEncryptionProgressAsync(
        string walletAddress, EncryptionSignal signal, CancellationToken ct = default) =>
        PublishEncryptionEventAsync(walletAddress, signal, EncryptionEventEnvelope.KindProgress, ct);

    /// <inheritdoc />
    public Task NotifyEncryptionCompleteAsync(
        string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
        => PublishEncryptionEventAsync(walletAddress, signal, EncryptionEventEnvelope.KindComplete, ct);

    /// <inheritdoc />
    public Task NotifyEncryptionFailedAsync(
        string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
        => PublishEncryptionEventAsync(walletAddress, signal, EncryptionEventEnvelope.KindFailed, ct);

    /// <summary>
    /// Publishes an encryption event to Redis. <c>EncryptionEventBridge</c> in
    /// Wallet Service consumes the channel and emits the signal on WalletHub.
    /// </summary>
    private async Task PublishEncryptionEventAsync(
        string walletAddress, EncryptionSignal signal, string kind, CancellationToken ct)
    {
        var envelope = new EncryptionEventEnvelope(
            WalletAddress: walletAddress,
            OperationId: signal.OperationId,
            Kind: kind,
            OccurredAt: DateTimeOffset.UtcNow,
            TraceId: CurrentTraceId());

        try
        {
            var subscriber = _redis.GetSubscriber();
            var json = JsonSerializer.Serialize(envelope, JsonOptions);
            await subscriber.PublishAsync(
                RedisChannel.Literal(EncryptionEventEnvelope.ChannelName), json);

            _logger.LogDebug(
                "Published encryption {Kind} for wallet {Wallet}, operation {OperationId}",
                kind, walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish encryption {Kind} envelope for wallet {Wallet}, operation {OperationId}",
                kind, walletAddress, signal.OperationId);
        }
    }

    private static string CurrentTraceId() =>
        Activity.Current?.TraceId.ToString() ?? string.Empty;
}
