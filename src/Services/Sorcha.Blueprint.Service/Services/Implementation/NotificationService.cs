// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
    private readonly IHubContext<BlueprintHub> _hubContext;
    private readonly IHubContext<EventsHub> _eventsHubContext;
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Initialises a new instance of the <see cref="NotificationService"/> class.</summary>
    public NotificationService(
        IHubContext<BlueprintHub> hubContext,
        IHubContext<EventsHub> eventsHubContext,
        ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _eventsHubContext = eventsHubContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyActionAvailableAsync(string instanceId, string? walletAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(walletAddress))
        {
            _logger.LogWarning(
                "Cannot send action-available signal for instance {InstanceId}: no wallet address linked",
                instanceId);
            return;
        }

        var signal = new SignalNotification
        {
            SignalType = SignalTypes.ActionAvailable,
            InstanceId = instanceId,
            CorrelationId = Guid.NewGuid()
        };

        await SendToWalletAsync(walletAddress, "ActionAvailable", signal, ct);

        _logger.LogInformation(
            "Sent signal {SignalType} to wallet {Wallet} for instance {InstanceId}",
            signal.SignalType, walletAddress, instanceId);
    }

    /// <inheritdoc />
    public async Task NotifyActionRejectedAsync(string instanceId, string? walletAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(walletAddress))
        {
            _logger.LogWarning(
                "Cannot send action-rejected signal for instance {InstanceId}: no wallet address linked",
                instanceId);
            return;
        }

        var signal = new SignalNotification
        {
            SignalType = SignalTypes.ActionRejected,
            InstanceId = instanceId,
            CorrelationId = Guid.NewGuid()
        };

        await SendToWalletAsync(walletAddress, "ActionRejected", signal, ct);

        _logger.LogInformation(
            "Sent signal {SignalType} to wallet {Wallet} for instance {InstanceId}",
            signal.SignalType, walletAddress, instanceId);
    }

    /// <inheritdoc />
    public async Task NotifyWorkflowCompletedAsync(string instanceId, IEnumerable<string> participantWalletAddresses, CancellationToken ct = default)
    {
        var signal = new SignalNotification
        {
            SignalType = SignalTypes.WorkflowCompleted,
            InstanceId = instanceId,
            CorrelationId = Guid.NewGuid()
        };

        foreach (var walletAddress in participantWalletAddresses)
        {
            if (string.IsNullOrEmpty(walletAddress))
                continue;

            try
            {
                await SendToWalletAsync(walletAddress, "WorkflowCompleted", signal, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send workflow-completed signal to wallet {Wallet} for instance {InstanceId}",
                    walletAddress, instanceId);
            }
        }

        _logger.LogInformation(
            "Sent signal {SignalType} for instance {InstanceId}",
            signal.SignalType, instanceId);
    }

    /// <inheritdoc />
    public async Task NotifyEncryptionProgressAsync(string walletAddress, EncryptionSignal signal, CancellationToken ct = default)
    {
        try
        {
            await SendToWalletAsync(walletAddress, "EncryptionProgress", signal, ct);

            _logger.LogDebug(
                "Sent EncryptionProgress to wallet {Wallet}. Operation: {OperationId}, {Percent}%",
                walletAddress, signal.OperationId, signal.PercentComplete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionProgress to wallet {Wallet}",
                walletAddress);
        }
    }

    /// <inheritdoc />
    public async Task NotifyEncryptionCompleteAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            await SendToWalletAsync(walletAddress, "EncryptionComplete", signal, ct);

            _logger.LogInformation(
                "Sent EncryptionComplete to wallet {Wallet}. Operation: {OperationId}",
                walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionComplete to wallet {Wallet}",
                walletAddress);
        }

        await SendEncryptionSignalToEventsHubAsync(userId, signal, ct);
    }

    /// <inheritdoc />
    public async Task NotifyEncryptionFailedAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default)
    {
        try
        {
            await SendToWalletAsync(walletAddress, "EncryptionFailed", signal, ct);

            _logger.LogWarning(
                "Sent EncryptionFailed to wallet {Wallet}. Operation: {OperationId}",
                walletAddress, signal.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionFailed to wallet {Wallet}",
                walletAddress);
        }

        await SendEncryptionSignalToEventsHubAsync(userId, signal, ct);
    }

    /// <summary>
    /// Sends an EncryptionSignal to the EventsHub for the specified user.
    /// Isolated in its own try/catch so BlueprintHub delivery is never affected.
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

    /// <summary>
    /// Send a signal to a wallet group via BlueprintHub.
    /// </summary>
    private async Task SendToWalletAsync(string walletAddress, string method, object signal, CancellationToken ct)
    {
        var groupName = BlueprintHubGroups.Wallet(walletAddress);
        await _hubContext.Clients
            .Group(groupName)
            .SendAsync(method, signal, ct);
    }
}
