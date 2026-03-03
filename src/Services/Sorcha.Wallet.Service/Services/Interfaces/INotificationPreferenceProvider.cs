// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Provides notification preferences for users.
/// Abstracts the Tenant Service preference lookup so delivery logic
/// is decoupled from the Tenant Service endpoint availability.
/// </summary>
public interface INotificationPreferenceProvider
{
    /// <summary>
    /// Get notification preferences for a user.
    /// </summary>
    /// <param name="userId">User identifier (sub claim).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User's notification preferences.</returns>
    Task<NotificationPreferences> GetPreferencesAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notification preferences for a user.
/// Maps to NotificationMethod and NotificationFrequency enums in Tenant Service.
/// </summary>
public record NotificationPreferences
{
    /// <summary>Whether notifications are enabled at all.</summary>
    public bool NotificationsEnabled { get; init; } = true;

    /// <summary>Whether to deliver in real-time or as a digest.</summary>
    public bool IsRealTime { get; init; } = true;

    /// <summary>Whether email delivery is requested (but transport may not be available).</summary>
    public bool WantsEmail { get; init; }

    /// <summary>Whether push delivery is requested (but transport may not be available).</summary>
    public bool WantsPush { get; init; }

    /// <summary>Default preferences: notifications enabled, real-time, in-app only.</summary>
    public static NotificationPreferences Default => new();
}
