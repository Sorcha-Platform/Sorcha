// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Service for broadcasting real-time notifications via SignalR.
/// </summary>
public class NotificationService : INotificationService
{
    private readonly IHubContext<ActionsHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IHubContext<ActionsHub> hubContext,
        ILogger<NotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Notify a wallet that a new action is available.
    /// </summary>
    public async Task NotifyActionAvailableAsync(ActionNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(notification.WalletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ActionAvailable", notification, ct);

            _logger.LogInformation(
                "Sent ActionAvailable notification to wallet {Wallet}. Transaction: {TxHash}",
                notification.WalletAddress,
                notification.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send ActionAvailable notification to wallet {Wallet}",
                notification.WalletAddress);
            throw;
        }
    }

    /// <summary>
    /// Notify a wallet that an action has been confirmed.
    /// </summary>
    public async Task NotifyActionConfirmedAsync(ActionNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(notification.WalletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ActionConfirmed", notification, ct);

            _logger.LogInformation(
                "Sent ActionConfirmed notification to wallet {Wallet}. Transaction: {TxHash}",
                notification.WalletAddress,
                notification.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send ActionConfirmed notification to wallet {Wallet}",
                notification.WalletAddress);
            throw;
        }
    }

    /// <summary>
    /// Notify a wallet that an action has been rejected.
    /// </summary>
    public async Task NotifyActionRejectedAsync(ActionNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(notification.WalletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ActionRejected", notification, ct);

            _logger.LogInformation(
                "Sent ActionRejected notification to wallet {Wallet}. Transaction: {TxHash}",
                notification.WalletAddress,
                notification.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send ActionRejected notification to wallet {Wallet}",
                notification.WalletAddress);
            throw;
        }
    }

    /// <summary>
    /// Notify a participant that a new action is available for them.
    /// </summary>
    public async Task NotifyActionAvailableAsync(
        string instanceId,
        int actionId,
        string actionTitle,
        string participantId,
        CancellationToken ct = default)
    {
        try
        {
            var groupName = GetInstanceGroupName(instanceId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ActionAvailable", new
                {
                    InstanceId = instanceId,
                    ActionId = actionId,
                    ActionTitle = actionTitle,
                    ParticipantId = participantId,
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);

            _logger.LogInformation(
                "Sent ActionAvailable notification for instance {InstanceId}, action {ActionId} to participant {ParticipantId}",
                instanceId, actionId, participantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send ActionAvailable notification for instance {InstanceId}, action {ActionId}",
                instanceId, actionId);
        }
    }

    /// <summary>
    /// Notify a participant that an action was rejected and routed to a target action.
    /// </summary>
    public async Task NotifyActionRejectedAsync(
        string instanceId,
        int rejectedActionId,
        int targetActionId,
        string targetParticipantId,
        string reason,
        CancellationToken ct = default)
    {
        try
        {
            var groupName = GetInstanceGroupName(instanceId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ActionRejected", new
                {
                    InstanceId = instanceId,
                    RejectedActionId = rejectedActionId,
                    TargetActionId = targetActionId,
                    TargetParticipantId = targetParticipantId,
                    Reason = reason,
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);

            _logger.LogInformation(
                "Sent ActionRejected notification for instance {InstanceId}, action {ActionId} rejected, routing to {TargetActionId}",
                instanceId, rejectedActionId, targetActionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send ActionRejected notification for instance {InstanceId}",
                instanceId);
        }
    }

    /// <summary>
    /// Notify all participants that a workflow has completed.
    /// </summary>
    public async Task NotifyWorkflowCompletedAsync(string instanceId, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetInstanceGroupName(instanceId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("WorkflowCompleted", new
                {
                    InstanceId = instanceId,
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);

            _logger.LogInformation(
                "Sent WorkflowCompleted notification for instance {InstanceId}",
                instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send WorkflowCompleted notification for instance {InstanceId}",
                instanceId);
        }
    }

    /// <summary>
    /// Notify a wallet about encryption progress.
    /// </summary>
    public async Task NotifyEncryptionProgressAsync(
        string walletAddress, EncryptionProgressNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(walletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("EncryptionProgress", notification, ct);

            _logger.LogDebug(
                "Sent EncryptionProgress to wallet {Wallet}. Step: {Step}/{Total} ({Percent}%)",
                walletAddress, notification.Step, notification.TotalSteps, notification.PercentComplete);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionProgress notification to wallet {Wallet}",
                walletAddress);
        }
    }

    /// <summary>
    /// Notify a wallet that encryption completed successfully.
    /// </summary>
    public async Task NotifyEncryptionCompleteAsync(
        string walletAddress, EncryptionCompleteNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(walletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("EncryptionComplete", notification, ct);

            _logger.LogInformation(
                "Sent EncryptionComplete to wallet {Wallet}. Operation: {OperationId}, TxHash: {TxHash}",
                walletAddress, notification.OperationId, notification.TransactionHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionComplete notification to wallet {Wallet}",
                walletAddress);
        }
    }

    /// <summary>
    /// Notify a wallet that encryption failed.
    /// </summary>
    public async Task NotifyEncryptionFailedAsync(
        string walletAddress, EncryptionFailedNotification notification, CancellationToken ct = default)
    {
        try
        {
            var groupName = GetWalletGroupName(walletAddress);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("EncryptionFailed", notification, ct);

            _logger.LogWarning(
                "Sent EncryptionFailed to wallet {Wallet}. Operation: {OperationId}, Error: {Error}",
                walletAddress, notification.OperationId, notification.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send EncryptionFailed notification to wallet {Wallet}",
                walletAddress);
        }
    }

    /// <summary>
    /// Get the SignalR group name for a wallet address.
    /// </summary>
    private static string GetWalletGroupName(string walletAddress)
    {
        return $"wallet:{walletAddress}";
    }

    /// <summary>
    /// Get the SignalR group name for a workflow instance.
    /// </summary>
    private static string GetInstanceGroupName(string instanceId)
    {
        return $"instance:{instanceId}";
    }
}
