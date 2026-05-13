// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for fetching and tracking service alerts from the API Gateway.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Fetches current alerts from the API Gateway.
    /// </summary>
    Task<AlertsResponse> GetAlertsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recently fetched alerts, or null if not yet loaded.
    /// </summary>
    AlertsResponse? CurrentAlerts { get; }

    /// <summary>
    /// Raised when the set of active alerts changes (new alerts appear or existing ones resolve).
    /// </summary>
    event EventHandler<AlertsChangedEventArgs>? AlertsChanged;
}

/// <summary>
/// Event args for alert state changes.
/// </summary>
public class AlertsChangedEventArgs : EventArgs
{
    public IReadOnlyList<ServiceAlert> NewAlerts { get; init; } = [];
    public IReadOnlyList<ServiceAlert> ResolvedAlerts { get; init; } = [];
}
