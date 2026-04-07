// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sorcha.ServiceClients.Participant;

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// SignalR hub for real-time action notifications.
/// </summary>
/// <remarks>
/// This hub enables real-time communication between the Blueprint Service and clients
/// for action availability, confirmation, and rejection notifications.
///
/// Connection URL: /actionshub
/// Authentication: JWT token via query parameter ?access_token={jwt}
///
/// Client Methods (called by server):
/// - ActionAvailable(notification): Notifies client when a new action is available
/// - ActionConfirmed(notification): Notifies client when an action is confirmed
/// - ActionRejected(notification): Notifies client when an action is rejected
///
/// Server Methods (called by clients):
/// - SubscribeToWallet(walletAddress): Subscribe to notifications for a wallet
/// - UnsubscribeFromWallet(walletAddress): Unsubscribe from wallet notifications
/// </remarks>
[Authorize]
public class ActionsHub : Hub
{
    private readonly ILogger<ActionsHub> _logger;
    private readonly IParticipantServiceClient _participantClient;

    public ActionsHub(ILogger<ActionsHub> logger, IParticipantServiceClient participantClient)
    {
        _logger = logger;
        _participantClient = participantClient;
    }

    /// <summary>
    /// Called when a client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        var userIdentifier = Context.UserIdentifier;

        _logger.LogInformation(
            "Client connected to ActionsHub. ConnectionId: {ConnectionId}, User: {User}",
            connectionId,
            userIdentifier ?? "anonymous");

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        var userIdentifier = Context.UserIdentifier;

        if (exception != null)
        {
            _logger.LogWarning(
                exception,
                "Client disconnected from ActionsHub with error. ConnectionId: {ConnectionId}, User: {User}",
                connectionId,
                userIdentifier ?? "anonymous");
        }
        else
        {
            _logger.LogInformation(
                "Client disconnected from ActionsHub. ConnectionId: {ConnectionId}, User: {User}",
                connectionId,
                userIdentifier ?? "anonymous");
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to notifications for a specific wallet address.
    /// Validates that the requesting user owns the wallet (via linked wallet addresses).
    /// Service tokens bypass ownership validation.
    /// </summary>
    /// <param name="walletAddress">The wallet address to subscribe to</param>
    public async Task SubscribeToWallet(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            throw new HubException("Wallet address cannot be empty");
        }

        // Service tokens can subscribe to any wallet but require org_id claim for scoping (SEC-AUDIT 3.4)
        var isServiceToken = Context.User?.Claims
            .Any(c => c.Type == "token_type" && c.Value == "service") == true;

        if (isServiceToken)
        {
            var orgId = Context.User?.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;
            _logger.LogInformation(
                "Service token subscription to wallet {Wallet}. ConnectionId: {ConnectionId}, OrgId: {OrgId}",
                walletAddress, Context.ConnectionId, orgId ?? "none");

            if (string.IsNullOrWhiteSpace(orgId))
            {
                _logger.LogWarning(
                    "Service token without org_id claim rejected for wallet {Wallet}. ConnectionId: {ConnectionId}",
                    walletAddress, Context.ConnectionId);
                throw new HubException("Unauthorized: service tokens must include org_id claim");
            }
        }
        else
        {
            await ValidateWalletOwnershipAsync(walletAddress);
        }

        var groupName = GetWalletGroupName(walletAddress);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client subscribed to wallet notifications. ConnectionId: {ConnectionId}, Wallet: {Wallet}",
            Context.ConnectionId,
            walletAddress);
    }

    /// <summary>
    /// Unsubscribe from notifications for a specific wallet address.
    /// </summary>
    /// <param name="walletAddress">The wallet address to unsubscribe from</param>
    public async Task UnsubscribeFromWallet(string walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            throw new HubException("Wallet address cannot be empty");
        }

        var groupName = GetWalletGroupName(walletAddress);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "Client unsubscribed from wallet notifications. ConnectionId: {ConnectionId}, Wallet: {Wallet}",
            Context.ConnectionId,
            walletAddress);
    }

    /// <summary>
    /// Validates that the connected user owns the specified wallet address
    /// by checking their linked wallets via the Participant Service.
    /// Fails closed: if the service is unavailable, subscription is denied.
    /// </summary>
    private async Task ValidateWalletOwnershipAsync(string walletAddress)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        var orgId = Context.User?.FindFirst("org_id")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(orgId))
        {
            throw new HubException("Unauthorized: missing identity claims");
        }

        if (!Guid.TryParse(userId, out var userGuid) || !Guid.TryParse(orgId, out var orgGuid))
        {
            throw new HubException("Unauthorized: invalid identity claims");
        }

        try
        {
            var participant = await _participantClient.GetByUserAndOrgAsync(userGuid, orgGuid);
            if (participant == null)
            {
                _logger.LogWarning(
                    "Wallet subscription denied: no participant found. User: {UserId}, Org: {OrgId}, Wallet: {Wallet}",
                    userId, orgId, walletAddress);
                throw new HubException("Unauthorized: wallet address not linked to your account");
            }

            var linkedWallets = await _participantClient.GetLinkedWalletsAsync(participant.Id, activeOnly: true);
            var ownsWallet = linkedWallets.Any(w =>
                string.Equals(w.WalletAddress, walletAddress, StringComparison.OrdinalIgnoreCase));

            if (!ownsWallet)
            {
                _logger.LogWarning(
                    "Wallet subscription denied: wallet not linked. User: {UserId}, Wallet: {Wallet}",
                    userId, walletAddress);
                throw new HubException("Unauthorized: wallet address not linked to your account");
            }
        }
        catch (HubException)
        {
            throw; // Re-throw our own HubExceptions
        }
        catch (Exception ex)
        {
            // Fail closed: if participant service is unavailable, deny subscription
            _logger.LogWarning(ex,
                "Wallet subscription denied: participant service unavailable. User: {UserId}, Wallet: {Wallet}",
                userId, walletAddress);
            throw new HubException("Unauthorized: unable to verify wallet ownership");
        }
    }

    /// <summary>
    /// Get the SignalR group name for a wallet address.
    /// </summary>
    private static string GetWalletGroupName(string walletAddress)
    {
        return $"wallet:{walletAddress}";
    }
}

/// <summary>
/// Well-known signal type constants for <see cref="SignalNotification.SignalType"/>.
/// </summary>
public static class SignalTypes
{
    public const string ActionAvailable = "action-available";
    public const string ActionRejected = "action-rejected";
    public const string WorkflowCompleted = "workflow-completed";
    public const string InboundAction = "inbound-action";
}

/// <summary>
/// Well-known status constants for <see cref="EncryptionSignal.Status"/>.
/// </summary>
public static class EncryptionStatuses
{
    public const string Encrypting = "encrypting";
    public const string Complete = "complete";
    public const string Failed = "failed";
}

/// <summary>
/// Thin signal notification sent to clients via SignalR.
/// Contains only signal type and instance identifier — clients pull details
/// through authenticated REST endpoints after receiving the signal.
/// Replaces the rich ActionNotification to enforce minimal disclosure.
/// </summary>
public sealed record SignalNotification
{
    /// <summary>
    /// Signal type: "action-available", "action-rejected", "workflow-completed", "inbound-action".
    /// </summary>
    public required string SignalType { get; init; }

    /// <summary>
    /// Blueprint instance identifier.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Optional correlation identifier for pull-back requests.
    /// </summary>
    public Guid? CorrelationId { get; init; }

    /// <summary>
    /// UTC timestamp of signal creation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
