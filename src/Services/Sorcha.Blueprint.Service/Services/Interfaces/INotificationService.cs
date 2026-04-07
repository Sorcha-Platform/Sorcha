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
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionAvailableAsync(string instanceId, string? walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Signal a wallet that an action was rejected.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="walletAddress">The participant's wallet address (null if not linked)</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyActionRejectedAsync(string instanceId, string? walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Signal all participants that a workflow has completed.
    /// </summary>
    /// <param name="instanceId">The workflow instance ID</param>
    /// <param name="participantWalletAddresses">Wallet addresses of all participants</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyWorkflowCompletedAsync(string instanceId, IEnumerable<string> participantWalletAddresses, CancellationToken ct = default);

    /// <summary>
    /// Signal encryption progress to the submitting wallet.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionProgressAsync(string walletAddress, EncryptionSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Signal encryption completion to the submitting wallet.
    /// Also sends to EventsHub for the user if userId is provided.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="userId">Optional user ID for EventsHub notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionCompleteAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Signal encryption failure to the submitting wallet.
    /// Also sends to EventsHub for the user if userId is provided.
    /// </summary>
    /// <param name="walletAddress">The submitting wallet address</param>
    /// <param name="signal">The encryption signal</param>
    /// <param name="userId">Optional user ID for EventsHub notification</param>
    /// <param name="ct">Cancellation token</param>
    Task NotifyEncryptionFailedAsync(string walletAddress, EncryptionSignal signal, string? userId = null, CancellationToken ct = default);
}
