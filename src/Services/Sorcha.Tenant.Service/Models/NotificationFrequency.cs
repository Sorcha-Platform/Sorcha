// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Delivery timing for inbound action notifications.
/// </summary>
public enum NotificationFrequency
{
    /// <summary>Immediate per-transaction delivery (default for new users).</summary>
    RealTime = 0,

    /// <summary>Batched hourly summary.</summary>
    HourlyDigest = 1,

    /// <summary>Batched daily summary.</summary>
    DailyDigest = 2
}
