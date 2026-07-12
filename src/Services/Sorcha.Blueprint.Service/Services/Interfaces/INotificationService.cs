// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Hubs;
using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Service for broadcasting thin signal notifications via SignalR.
/// Signals contain minimal metadata — clients pull details through authenticated endpoints.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Signal a wallet that a new action is available.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="walletAddress">The participant's wallet address (null if not linked)</param>
    /// <param name="actionId">Optional action identifier to surface on the wire signal.</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionAvailableAsync(string instanceId, string? walletAddress, string? actionId = null, CancellationToken ct = default);

    /// <summary>
    /// Signal a wallet that an action was rejected.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="walletAddress">The participant's wallet address (null if not linked)</param>
    /// <param name="actionId">Optional action identifier to surface on the wire signal.</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionRejectedAsync(string instanceId, string? walletAddress, string? actionId = null, CancellationToken ct = default);

    /// <summary>
    /// Signal all participants that a workflow has completed.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="participantWalletAddresses">Wallet addresses of all participants</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyWorkflowCompletedAsync(string instanceId, IEnumerable<string> participantWalletAddresses, CancellationToken ct = default);

    /// <summary>
    /// Write a durable "decision" inbox notice to a participant (Feature 183). Backs the
    /// <c>x-decision-notice</c> route annotation — the on-brand reason is carried so a rejected
    /// applicant can see WHY, durably, across sessions and devices. Best-effort: a write failure
    /// is swallowed and never affects sealing or routing.
    /// </summary>
    /// <param name="recipientWalletAddress">The recipient participant's wallet address.</param>
    /// <param name="instanceId">The workflow instance ID.</param>
    /// <param name="actionId">The action that produced the decision.</param>
    /// <param name="title">Notification title.</param>
    /// <param name="reason">On-brand reason surfaced as the notification summary.</param>
    /// <param name="severity">Inbox severity (defaults to <c>Warning</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    Task NotifyDecisionAsync(
        string recipientWalletAddress,
        string instanceId,
        string actionId,
        string title,
        string reason,
        string severity = "Warning",
        CancellationToken ct = default);

    /// <summary>
    /// Signal encryption progress to the submitting wallet.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionProgressAsync(string walletAddress, EncryptionSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Signal encryption completion to the submitting wallet via the WalletHub
    /// Redis bridge.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="userId">Unused. Retained for API stability while encryption call sites finish migrating.</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionCompleteAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Signal encryption failure to the submitting wallet via the WalletHub
    /// Redis bridge.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="userId">Unused. Retained for API stability while encryption call sites finish migrating.</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionFailedAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default);
}
