// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Sliding window rate limiter for notification delivery.
/// Caps notifications at a configurable rate per user (default: 10/min)
/// to prevent burst scenarios from overwhelming users.
/// Rate-limited notifications overflow to digest delivery.
/// </summary>
public interface INotificationRateLimiter
{
    /// <summary>
    /// Check if a notification is allowed for the given user within the rate limit window.
    /// If allowed, increments the counter atomically.
    /// </summary>
    /// <param name="userId">User identifier to rate-limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the notification is within rate limits; false if rate-limited.</returns>
    Task<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current notification count for a user in the current window.
    /// </summary>
    /// <param name="userId">User identifier to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current count of notifications in the active window.</returns>
    Task<int> GetCurrentCountAsync(string userId, CancellationToken cancellationToken = default);
}
