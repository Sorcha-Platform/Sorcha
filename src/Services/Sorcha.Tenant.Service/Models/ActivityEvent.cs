// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents a user or system event captured for the activity log.
/// Stored in PostgreSQL via TenantDbContext.
/// </summary>
public class ActivityEvent
{
    /// <summary>Unique identifier for the resource.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the organization that owns this resource.</summary>
    public Guid OrganizationId { get; set; }
    /// <summary>Identifier of the user.</summary>
    public Guid UserId { get; set; }
    /// <summary>The event type.</summary>
    public required string EventType { get; set; }
    /// <summary>The severity.</summary>
    public EventSeverity Severity { get; set; }
    /// <summary>Human-readable title.</summary>
    public required string Title { get; set; }
    /// <summary>Human-readable message.</summary>
    public required string Message { get; set; }
    /// <summary>The source service.</summary>
    public required string SourceService { get; set; }
    /// <summary>Identifier of the entity.</summary>
    public string? EntityId { get; set; }
    /// <summary>The entity type.</summary>
    public string? EntityType { get; set; }
    /// <summary>Indicates whether read.</summary>
    public bool IsRead { get; set; }
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Timestamp at which the record expires (UTC).</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Severity level for activity events.
/// </summary>
public enum EventSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
