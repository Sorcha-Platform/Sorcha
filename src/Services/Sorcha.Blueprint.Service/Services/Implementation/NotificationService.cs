// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for broadcasting thin signal notifications via SignalR.
/// All signals are delivered exclusively through wallet-scoped groups.
/// Clients pull detailed data through authenticated REST endpoints.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IHubContext<BlueprintHub, IBlueprintHubClient> _hubContext;
    private readonly IHubContext<EventsHub> _eventsHubContext;
    private readonly IBlueprintInboxWriter _inboxWriter;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Initialises a new instance of the <see cref="NotificationService"/> class.</summary>
    public NotificationService(
        IHubContext<BlueprintHub, IBlueprintHubClient> hubContext,
        IHubContext<EventsHub> eventsHubContext,
        IBlueprintInboxWriter inboxWriter,
        ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _eventsHubContext = eventsHubContext;
        _inboxWriter = inboxWriter;
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
    public async Task NotifyEncryptionProgressAsync(
        string walletAddress, EncryptionSignal signal, CancellationToken ct = default)
    {
        try
        {
            var groupName = BlueprintHubGroups.Wallet(walletAddress);
            await _hubContext.Clients.Group(groupName)
                .EncryptionProgress(signal.OperationId, DateTimeOffset.UtcNow, CurrentTraceId());

            _logger.LogDebug(
                "Sent EncryptionProgress to wallet {Wallet}. Operation: {OperationId}",
                walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionProgress to wallet {Wallet}", walletAddress);
        }
    }

    /// <inheritdoc />
    public async Task NotifyEncryptionCompleteAsync(
        string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            var groupName = BlueprintHubGroups.Wallet(walletAddress);
            await _hubContext.Clients.Group(groupName)
                .EncryptionComplete(signal.OperationId, DateTimeOffset.UtcNow, CurrentTraceId());

            _logger.LogInformation(
                "Sent EncryptionComplete to wallet {Wallet}. Operation: {OperationId}",
                walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionComplete to wallet {Wallet}", walletAddress);
        }

        await SendEncryptionSignalToEventsHubAsync(userId, signal, ct);
    }

    /// <inheritdoc />
    public async Task NotifyEncryptionFailedAsync(
        string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            var groupName = BlueprintHubGroups.Wallet(walletAddress);
            await _hubContext.Clients.Group(groupName)
                .EncryptionFailed(signal.OperationId, DateTimeOffset.UtcNow, CurrentTraceId());

            _logger.LogWarning(
                "Sent EncryptionFailed to wallet {Wallet}. Operation: {OperationId}",
                walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionFailed to wallet {Wallet}", walletAddress);
        }

        await SendEncryptionSignalToEventsHubAsync(userId, signal, ct);
    }

    /// <summary>
    /// Sends an EncryptionSignal to the legacy EventsHub for the specified user.
    /// Isolated in its own try/catch so BlueprintHub delivery is never affected.
    /// EventsHub is exempt from the thin-signal contract (Phase 10 retire).
    /// </summary>
    private async Task SendEncryptionSignalToEventsHubAsync(
        string? userId, EncryptionSignal signal, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug(
                "No userId provided for EventsHub encryption signal. Operation: {OperationId}",
                signal.OperationId);
            return;
        }

        try
        {
            var userGroup = BlueprintHubGroups.LegacyEventsHubUser(userId);
            await _eventsHubContext.Clients
                .Group(userGroup)
                .SendAsync("EncryptionOperationCompleted", signal, ct);

            _logger.LogInformation(
                "Sent EncryptionSignal to EventsHub user:{UserId}. Operation: {OperationId}, Status: {Status}",
                userId, signal.OperationId, signal.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionSignal to EventsHub for user {UserId}. Operation: {OperationId}",
                userId, signal.OperationId);
        }
    }

    private static string CurrentTraceId() =>
        Activity.Current?.TraceId.ToString() ?? string.Empty;
}
