// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ApiGateway.Models;

/// <summary>
/// Aggregated health response from all services
/// </summary>
public class AggregatedHealthResponse
{
    /// <summary>Current status of the resource.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Map of services keyed by string.</summary>
    public Dictionary<string, ServiceHealth> Services { get; set; } = new();
}

/// <summary>
/// Health status of an individual service
/// </summary>
public class ServiceHealth
{
    /// <summary>Current status of the resource.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Endpoint URL.</summary>
    public string? Endpoint { get; set; }
    /// <summary>Error details when the operation did not succeed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// System-wide statistics
/// </summary>
public class SystemStatistics
{
    /// <summary>Numeric value for total services.</summary>
    public int TotalServices { get; set; }
    /// <summary>Numeric value for healthy services.</summary>
    public int HealthyServices { get; set; }
    /// <summary>Numeric value for unhealthy services.</summary>
    public int UnhealthyServices { get; set; }
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }
    /// <summary>Map of service metrics keyed by string.</summary>
    public Dictionary<string, object> ServiceMetrics { get; set; } = new();
}
