// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default notification preference provider that returns RealTime + InApp for all users.
/// This matches the Tenant Service UserPreferences entity defaults (InApp + RealTime).
/// Replace with a Tenant Service-aware implementation in Phase 5 (US3)
/// when the GET /api/preferences endpoint exposes NotificationMethod and NotificationFrequency.
/// </summary>
public sealed class DefaultNotificationPreferenceProvider : INotificationPreferenceProvider
{
    private readonly ILogger<DefaultNotificationPreferenceProvider> _logger;

    public DefaultNotificationPreferenceProvider(ILogger<DefaultNotificationPreferenceProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<NotificationPreferences> GetPreferencesAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Using default notification preferences for user {UserId} (RealTime + InApp)",
            userId);
        return Task.FromResult(NotificationPreferences.Default);
    }
}
