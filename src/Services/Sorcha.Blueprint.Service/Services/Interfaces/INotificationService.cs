// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Service for broadcasting real-time notifications via SignalR.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Notify a wallet that a new action is available.
    /// </summary>
    /// <param name="notification">The action notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionAvailableAsync(ActionNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Notify a wallet that an action has been confirmed.
    /// </summary>
    /// <param name="notification">The action notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionConfirmedAsync(ActionNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Notify a wallet that an action has been rejected.
    /// </summary>
    /// <param name="notification">The action notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionRejectedAsync(ActionNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Notify a participant that a new action is available for them.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="actionId">The action ID</param>
    /// <param name="actionTitle">The action title</param>
    /// <param name="participantId">The participant who should execute the action</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionAvailableAsync(
        string instanceId,
        int actionId,
        string actionTitle,
        string participantId,
        CancellationToken ct = default);

    /// <summary>
    /// Notify a participant that an action was rejected and routed to a target action.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="rejectedActionId">The action that was rejected</param>
    /// <param name="targetActionId">The action the workflow routes to</param>
    /// <param name="targetParticipantId">The participant who receives the rejection</param>
    /// <param name="reason">The rejection reason</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionRejectedAsync(
        string instanceId,
        int rejectedActionId,
        int targetActionId,
        string targetParticipantId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Notify all participants that a workflow has completed.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyWorkflowCompletedAsync(string instanceId, CancellationToken ct = default);

    /// <summary>
    /// Notify a wallet about encryption progress.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="notification">The progress notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionProgressAsync(string walletAddress, EncryptionProgressNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Notify a wallet that encryption completed successfully.
    /// Also sends an EncryptionOperationCompleted event to EventsHub for the user.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="notification">The completion notification</param>
    /// <param name="userId">Optional user ID for EventsHub notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionCompleteAsync(string walletAddress, EncryptionCompleteNotification notification, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Notify a wallet that encryption failed.
    /// Also sends an EncryptionOperationCompleted event to EventsHub for the user.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="notification">The failure notification</param>
    /// <param name="userId">Optional user ID for EventsHub notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionFailedAsync(string walletAddress, EncryptionFailedNotification notification, string? userId = null, CancellationToken ct = default);
}
