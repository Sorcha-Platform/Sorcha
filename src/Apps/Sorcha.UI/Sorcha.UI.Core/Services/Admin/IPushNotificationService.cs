// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Service for managing push notification subscriptions.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Gets the current push subscription status.
    /// </summary>
    Task<PushSubscriptionStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Subscribes to push notifications with the given browser subscription details.
    /// </summary>
    Task<bool> SubscribeAsync(PushSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Unsubscribes from push notifications for the given endpoint.
    /// </summary>
    Task<bool> UnsubscribeAsync(string endpoint, CancellationToken ct = default);
}
