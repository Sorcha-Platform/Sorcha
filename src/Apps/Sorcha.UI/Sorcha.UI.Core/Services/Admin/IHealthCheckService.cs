// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Event arguments for health status changes.
/// </summary>
public class HealthStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// The service key that changed status.
    /// </summary>
    public required string ServiceKey { get; init; }

    /// <summary>
    /// Previous health status.
    /// </summary>
    public required HealthStatus OldStatus { get; init; }

    /// <summary>
    /// New health status.
    /// </summary>
    public required HealthStatus NewStatus { get; init; }
}

/// <summary>
/// Service for monitoring health of platform services via polling.
/// </summary>
public interface IHealthCheckService : IAsyncDisposable
{
    /// <summary>
    /// Current health status of all monitored services.
    /// </summary>
    IReadOnlyList<ServiceHealthStatus> ServiceStatuses { get; }

    /// <summary>
    /// Event raised when any service health status changes.
    /// </summary>
    event EventHandler<HealthStatusChangedEventArgs>? HealthStatusChanged;

    /// <summary>
    /// Start polling health endpoints at the configured interval.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop polling and clean up resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Force an immediate health check of all services.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get health status for a specific service.
    /// </summary>
    /// <param name="serviceKey">The service identifier key.</param>
    /// <returns>The service health status, or null if not found.</returns>
    ServiceHealthStatus? GetServiceStatus(string serviceKey);
}
