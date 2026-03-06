// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Service health status.
/// </summary>
public class ServiceHealthStatus
{
    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("responseTimeMs")]
    public int ResponseTimeMs { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("checkedAt")]
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>
/// Aggregate health response for all services.
/// </summary>
public class HealthCheckResponse
{
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = string.Empty;

    [JsonPropertyName("services")]
    public List<ServiceHealthStatus> Services { get; set; } = new();

    [JsonPropertyName("checkedAt")]
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>
/// Schema sector information.
/// </summary>
public class SchemaSector
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("schemaCount")]
    public int SchemaCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Schema provider information.
/// </summary>
public class SchemaProvider
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("schemaCount")]
    public int SchemaCount { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Detailed schema provider information including health and refresh status.
/// </summary>
public class SchemaProviderDetail
{
    [JsonPropertyName("providerName")]
    public string ProviderName { get; set; } = string.Empty;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("baseUri")]
    public string BaseUri { get; set; } = string.Empty;

    [JsonPropertyName("providerType")]
    public string ProviderType { get; set; } = string.Empty;

    [JsonPropertyName("rateLimitPerSecond")]
    public double RateLimitPerSecond { get; set; }

    [JsonPropertyName("refreshIntervalHours")]
    public int RefreshIntervalHours { get; set; }

    [JsonPropertyName("lastSuccessfulFetch")]
    public DateTimeOffset? LastSuccessfulFetch { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    [JsonPropertyName("lastErrorAt")]
    public DateTimeOffset? LastErrorAt { get; set; }

    [JsonPropertyName("schemaCount")]
    public int SchemaCount { get; set; }

    [JsonPropertyName("healthStatus")]
    public string HealthStatus { get; set; } = string.Empty;

    [JsonPropertyName("backoffUntil")]
    public DateTimeOffset? BackoffUntil { get; set; }

    [JsonPropertyName("consecutiveFailures")]
    public int ConsecutiveFailures { get; set; }
}

/// <summary>
/// System alert.
/// </summary>
public class SystemAlert
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("acknowledgedAt")]
    public DateTimeOffset? AcknowledgedAt { get; set; }

    [JsonPropertyName("resolvedAt")]
    public DateTimeOffset? ResolvedAt { get; set; }
}

/// <summary>
/// System event from the events admin API.
/// </summary>
public class CliSystemEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
}

/// <summary>
/// Encryption operation status from the Blueprint Service.
/// </summary>
public class EncryptionOperationStatus
{
    [JsonPropertyName("operationId")] public string OperationId { get; set; } = string.Empty;
    [JsonPropertyName("stage")] public string Stage { get; set; } = string.Empty;
    [JsonPropertyName("percentComplete")] public int PercentComplete { get; set; }
    [JsonPropertyName("recipientCount")] public int RecipientCount { get; set; }
    [JsonPropertyName("processedRecipients")] public int ProcessedRecipients { get; set; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
}

/// <summary>
/// Paginated event list response.
/// </summary>
public class CliEventListResponse
{
    [JsonPropertyName("events")]
    public List<CliSystemEvent> Events { get; set; } = new();

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
}
