// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="EventsHub"/>. Bridge interface added
/// in Feature 118 Phase 3 (US1 — multi-node correctness).
/// </summary>
/// <remarks>
/// <para>
/// EventsHub is retired in Phase 4 (US2). Its workflow events fold into
/// <c>BlueprintHub</c>, its wallet events fold into <c>WalletHub</c>, its
/// user-feed events become inbox entries on <c>TenantHub</c>. This interface
/// mirrors the existing untyped <c>SendAsync</c> method names emitted by
/// <c>EventsHubNotificationBridge</c> so the parallel-fire window during US2
/// can keep working until subscribers migrate.
/// </para>
/// </remarks>
public interface IEventsHubClient
{
    /// <summary>Generic activity event item pushed to the user's activity feed.</summary>
    Task EventReceived(object activityEvent);

    /// <summary>The user's unread-count value changed.</summary>
    Task UnreadCountUpdated(int unreadCount);

    /// <summary>An encryption operation completed (success or failure).</summary>
    Task EncryptionOperationCompleted(object signal);

    /// <summary>An inbound action arrived for the user.</summary>
    Task InboundActionReceived(SignalNotification signal);

    /// <summary>A digest notification (multiple events grouped) arrived.</summary>
    Task DigestNotificationReceived(string json);

    /// <summary>A credential issued to the user changed status.</summary>
    Task CredentialStatusChanged(object evt);
}
