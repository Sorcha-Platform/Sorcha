// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents a user or system event captured for the activity log.
/// Stored in PostgreSQL via TenantDbContext.
/// </summary>
public class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public required string EventType { get; set; }
    public EventSeverity Severity { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string SourceService { get; set; }
    public string? EntityId { get; set; }
    public string? EntityType { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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
