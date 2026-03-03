// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Delivery channel for inbound action notifications.
/// </summary>
public enum NotificationMethod
{
    /// <summary>In-app notifications only (default for new users).</summary>
    InApp = 0,

    /// <summary>In-app notifications plus email summary.</summary>
    InAppPlusEmail = 1,

    /// <summary>In-app notifications plus browser push notification.</summary>
    InAppPlusPush = 2
}
