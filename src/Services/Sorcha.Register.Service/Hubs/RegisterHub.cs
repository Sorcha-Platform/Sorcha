// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Sorcha.ServiceClients.Subscription;

namespace Sorcha.Register.Service.Hubs;

/// <summary>
/// SignalR hub for real-time register notifications.
/// Subscription access is verified before allowing clients to join register groups.
/// </summary>
public class RegisterHub : Hub<IRegisterHubClient>
{
    private readonly ISubscriptionServiceClient _subscriptionClient;

    /// <summary>
    /// Initializes the hub with subscription client for access checks
    /// </summary>
    public RegisterHub(ISubscriptionServiceClient subscriptionClient)
    {
        _subscriptionClient = subscriptionClient;
    }

    /// <summary>
    /// Subscribe to register updates. Verifies the caller's org has an active subscription.
    /// </summary>
    public async Task SubscribeToRegister(string registerId)
    {
        var orgIdClaim = Context.User?.FindFirst("org_id")?.Value;
        if (!string.IsNullOrEmpty(orgIdClaim) && Guid.TryParse(orgIdClaim, out var orgId))
        {
            var subscribedIds = await _subscriptionClient.GetActiveRegisterIdsForOrgAsync(orgId);
            if (!subscribedIds.Contains(registerId, StringComparer.OrdinalIgnoreCase))
            {
                // Not subscribed — deny the group join silently
                return;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, RegisterHubGroups.Register(registerId));
    }

    /// <summary>
    /// Unsubscribe from register updates
    /// </summary>
    public async Task UnsubscribeFromRegister(string registerId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, RegisterHubGroups.Register(registerId));
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Client methods that can be invoked by the hub
/// </summary>
public interface IRegisterHubClient
{
    Task RegisterCreated(string registerId, string name);
    Task RegisterDeleted(string registerId);
    Task RegisterStatusChanged(string registerId, string status);
    Task TransactionConfirmed(string registerId, string transactionId);
    Task DocketSealed(string registerId, ulong docketId, string hash);
    Task RegisterHeightUpdated(string registerId, uint newHeight);
    Task RegisterSyncStateChanged(string registerId, string syncState);
    Task TransactionReceipt(string transactionId, string registerId, long docketNumber, string receiptId, DateTimeOffset sealedAt);
}
