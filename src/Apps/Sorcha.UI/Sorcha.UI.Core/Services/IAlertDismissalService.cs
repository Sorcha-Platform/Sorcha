// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for per-user alert dismissal using browser localStorage.
/// </summary>
public interface IAlertDismissalService
{
    /// <summary>
    /// Marks an alert as dismissed for the current user.
    /// </summary>
    Task DismissAlertAsync(string alertId);

    /// <summary>
    /// Returns whether the given alert has been dismissed by the current user.
    /// </summary>
    Task<bool> IsAlertDismissedAsync(string alertId);

    /// <summary>
    /// Filters out dismissed alerts from the given list.
    /// </summary>
    Task<IReadOnlyList<ServiceAlert>> FilterDismissedAsync(IReadOnlyList<ServiceAlert> alerts);

    /// <summary>
    /// Clears all dismissed alert records for the current user.
    /// </summary>
    Task ClearDismissedAlertsAsync();
}
