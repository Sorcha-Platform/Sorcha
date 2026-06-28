// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for administrative operations via the API Gateway.
/// </summary>
public interface IAdminServiceClient
{
    /// <summary>
    /// Gets the health status of all services.
    /// </summary>
    [Get("/api/health")]
    Task<HealthCheckResponse> GetHealthAsync([Header("Authorization")] string authorization);

    /// <summary>
    /// Lists system alerts.
    /// </summary>
    [Get("/api/alerts")]
    Task<List<SystemAlert>> ListAlertsAsync([Query] string? severity, [Header("Authorization")] string authorization);

}
