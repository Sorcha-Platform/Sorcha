// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.Models;

/// <summary>
/// Feature 106 — emitted by the Wallet Service when a credential transitions status
/// (e.g. <c>PendingAcceptance → Active</c> after the holder clicks Accept, or
/// <c>PendingAcceptance → Declined</c> after Decline). Published to the Redis
/// <c>wallet:credential-status</c> channel and forwarded to the holder's SignalR
/// group by the Blueprint Service's <c>EventsHubNotificationBridge</c>.
/// </summary>
/// <remarks>
/// Contract: <c>specs/106-register-native-credentials/contracts/credential-status-enum.md §SignalR notification shape</c>.
/// The richer initial-arrival notification (<c>InboundActionEvent.CredentialOfferId</c>)
/// is already emitted by <c>NotificationDeliveryService</c>. This event covers
/// every subsequent transition so UI caches (MyCredentials + MyActions) can
/// refresh without polling.
/// </remarks>
public sealed record CredentialStatusChangedEvent
{
    /// <summary>Wallet address that holds the credential.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>Unique credential identifier (URN / DID URI).</summary>
    public required string CredentialId { get; init; }

    /// <summary>Credential type — e.g. <c>AssuredIdentityCredential</c>.</summary>
    public required string CredentialType { get; init; }

    /// <summary>Status immediately before the transition.</summary>
    public required string PreviousStatus { get; init; }

    /// <summary>Status after the transition. Persisted to the wallet store at emit time.</summary>
    public required string NewStatus { get; init; }

    /// <summary>UTC timestamp of the transition.</summary>
    public required DateTimeOffset ChangedAt { get; init; }

    /// <summary>
    /// Owning user id (from <c>Wallet.Owner</c>) — used by the SignalR bridge to route the
    /// event to the right user group. Nullable so callers that only have the wallet
    /// address can still publish; the bridge can resolve the owner by wallet lookup.
    /// </summary>
    public string? UserId { get; init; }
}
