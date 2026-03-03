// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default notification preference provider that returns RealTime + InApp for all users.
/// This matches the Tenant Service UserPreferences entity defaults (InApp + RealTime).
/// </summary>
/// <remarks>
/// TODO: Replace with a TenantNotificationPreferenceProvider that calls
/// Tenant Service GET /api/users/{userId}/preferences to read the user's actual
/// NotificationMethod and NotificationFrequency settings. Until then, all users
/// receive real-time in-app notifications regardless of their saved preferences.
/// </remarks>
public sealed class DefaultNotificationPreferenceProvider : INotificationPreferenceProvider
{
    private readonly ILogger<DefaultNotificationPreferenceProvider> _logger;
    private bool _warningLogged;

    public DefaultNotificationPreferenceProvider(ILogger<DefaultNotificationPreferenceProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<NotificationPreferences> GetPreferencesAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        if (!_warningLogged)
        {
            _logger.LogWarning(
                "Using DefaultNotificationPreferenceProvider — all users receive RealTime+InApp. " +
                "Replace with TenantNotificationPreferenceProvider to honour user preferences");
            _warningLogged = true;
        }

        _logger.LogDebug(
            "Using default notification preferences for user {UserId} (RealTime + InApp)",
            userId);
        return Task.FromResult(NotificationPreferences.Default);
    }
}
