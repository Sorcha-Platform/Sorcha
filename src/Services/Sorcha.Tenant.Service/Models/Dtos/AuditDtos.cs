// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Response representing a single audit log entry.
/// </summary>
public record AuditEventResponse
{
    /// <summary>
    /// Audit entry ID.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Event timestamp (UTC).
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Type of audit event.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// User or service identity ID associated with this event.
    /// </summary>
    public Guid? IdentityId { get; init; }

    /// <summary>
    /// Client IP address.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Whether the event succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Additional event-specific details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// Maps from an AuditLogEntry entity.
    /// </summary>
    public static AuditEventResponse FromEntity(AuditLogEntry entry) => new()
    {
        Id = entry.Id,
        Timestamp = entry.Timestamp,
        EventType = entry.EventType.ToString(),
        IdentityId = entry.IdentityId,
        IpAddress = entry.IpAddress,
        Success = entry.Success,
        Details = entry.Details
    };
}

/// <summary>
/// Paginated response for audit log queries.
/// </summary>
public record AuditLogResponse
{
    /// <summary>
    /// List of audit events for the current page.
    /// </summary>
    public List<AuditEventResponse> Events { get; init; } = [];

    /// <summary>
    /// Total number of matching events.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Number of events per page.
    /// </summary>
    public int PageSize { get; init; }
}

/// <summary>
/// Response containing audit retention configuration.
/// </summary>
public record AuditRetentionResponse
{
    /// <summary>
    /// Audit log retention period in months (1-120).
    /// </summary>
    public int RetentionMonths { get; init; }
}

/// <summary>
/// Request to update audit retention period.
/// </summary>
public record UpdateAuditRetentionRequest
{
    /// <summary>
    /// Audit log retention period in months (1-120).
    /// </summary>
    public int RetentionMonths { get; init; }
}
