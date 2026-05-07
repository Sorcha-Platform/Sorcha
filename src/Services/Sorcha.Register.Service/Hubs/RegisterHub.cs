// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Sorcha.ServiceClients.Subscription;
using Sorcha.ServiceDefaults.Hubs;

namespace Sorcha.Register.Service.Hubs;

/// <summary>
/// SignalR hub for real-time register notifications.
/// Subscription access is verified before allowing clients to join register groups.
/// </summary>
public class RegisterHub : Hub<IRegisterHubClient>
{
    private readonly ISubscriptionServiceClient _subscriptionClient;
    private readonly SignalRMetrics _metrics;

    /// <summary>
    /// Initializes the hub with subscription client for access checks
    /// </summary>
    public RegisterHub(ISubscriptionServiceClient subscriptionClient, SignalRMetrics metrics)
    {
        _subscriptionClient = subscriptionClient;
        _metrics = metrics;
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

    /// <summary>
    /// Records the connection in <see cref="SignalRMetrics.ConnectionsTotal"/>
    /// tagged by authentication state. Drives the cutover gauge per Feature 118
    /// task T090 — RegisterHub adds <c>[Authorize]</c> only after the
    /// authenticated tag reaches ≥ 99 % of total.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var authenticated = Context.User?.Identity?.IsAuthenticated == true;
        _metrics.ConnectionsTotal.Add(1,
            new KeyValuePair<string, object?>("hub", "register"),
            new KeyValuePair<string, object?>("state", "connected"),
            new KeyValuePair<string, object?>("authenticated", authenticated));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>
/// Typed client interface for <see cref="RegisterHub"/>. Conforms to the
/// Feature 118 thin-signal contract — clients fetch full detail through
/// authenticated REST endpoints referenced in each method's <c>&lt;see cref&gt;</c>
/// doc.
/// </summary>
public interface IRegisterHubClient
{
    /// <summary>
    /// A register was created. Clients fetch full detail via
    /// <c>GET /api/registers/{registerId}</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the new register.</param>
    /// <param name="name">Display name of the register at create-time.</param>
    Task RegisterCreated(string registerId, string name);

    /// <summary>
    /// A register was deleted. Clients SHOULD remove cached state for the id;
    /// <c>GET /api/registers/{registerId}</c> will return 404.
    /// </summary>
    /// <param name="registerId">Identifier of the deleted register.</param>
    Task RegisterDeleted(string registerId);

    /// <summary>
    /// A register's status changed. Clients fetch the current status via
    /// <c>GET /api/registers/{registerId}</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the register whose status changed.</param>
    /// <param name="status">New status string (e.g. "active", "suspended").</param>
    Task RegisterStatusChanged(string registerId, string status);

    /// <summary>
    /// A transaction was confirmed in a register. Clients fetch full
    /// transaction detail via
    /// <c>GET /api/registers/{registerId}/transactions/{transactionId}</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the register.</param>
    /// <param name="transactionId">Identifier of the confirmed transaction.</param>
    Task TransactionConfirmed(string registerId, string transactionId);

    /// <summary>
    /// A docket was sealed. Clients fetch docket detail via
    /// <c>GET /api/registers/{registerId}/dockets/{docketId}</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the register.</param>
    /// <param name="docketId">Sealed docket sequence number.</param>
    /// <param name="hash">Hex-encoded SHA-256 docket hash.</param>
    Task DocketSealed(string registerId, ulong docketId, string hash);

    /// <summary>
    /// A register's height advanced. Clients fetch authoritative height via
    /// <c>GET /api/registers/{registerId}</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the register.</param>
    /// <param name="newHeight">New register height.</param>
    Task RegisterHeightUpdated(string registerId, uint newHeight);

    /// <summary>
    /// A register's local sync state changed. Clients fetch authoritative
    /// state via <c>GET /api/registers/{registerId}/sync-state</c>.
    /// </summary>
    /// <param name="registerId">Identifier of the register.</param>
    /// <param name="syncState">New sync state (Indeterminate / Syncing / CaughtUp / Error).</param>
    Task RegisterSyncStateChanged(string registerId, string syncState);

    /// <summary>
    /// A transaction receipt was issued. Clients fetch the receipt via
    /// <c>GET /api/registers/{registerId}/transactions/{transactionId}/receipt</c>.
    /// </summary>
    /// <param name="transactionId">Identifier of the transaction.</param>
    /// <param name="registerId">Identifier of the register.</param>
    /// <param name="docketNumber">Sealed docket sequence number.</param>
    /// <param name="receiptId">Identifier of the issued receipt.</param>
    /// <param name="sealedAt">Server timestamp at which the docket was sealed.</param>
    Task TransactionReceipt(string transactionId, string registerId, long docketNumber, string receiptId, DateTimeOffset sealedAt);
}
