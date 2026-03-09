// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side audit service for logging administrative actions.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an audit event.
    /// </summary>
    /// <param name="eventType">Type of audit event.</param>
    /// <param name="details">Event-specific details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(
        AuditEventType eventType,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an organization-related audit event.
    /// </summary>
    /// <param name="eventType">Type of audit event.</param>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="details">Event-specific details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogOrganizationEventAsync(
        AuditEventType eventType,
        Guid organizationId,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs a user-related audit event.
    /// </summary>
    /// <param name="eventType">Type of audit event.</param>
    /// <param name="organizationId">Organization ID.</param>
    /// <param name="userId">User ID.</param>
    /// <param name="details">Event-specific details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogUserEventAsync(
        AuditEventType eventType,
        Guid organizationId,
        Guid userId,
        Dictionary<string, object>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries audit events for an organization with filtering and pagination.
    /// </summary>
    Task<AuditQueryResult> QueryAuditEventsAsync(
        Guid organizationId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        string? eventType = null,
        Guid? userId = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the audit retention configuration for an organization.
    /// </summary>
    Task<AuditRetentionDto> GetRetentionAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the audit retention configuration for an organization.
    /// </summary>
    Task<bool> UpdateRetentionAsync(
        Guid organizationId,
        int retentionMonths,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Types of audit events for admin operations.
/// Mirrors the backend AuditEventType enum with admin-specific additions.
/// </summary>
public enum AuditEventType
{
    /// <summary>
    /// Organization was created.
    /// </summary>
    OrganizationCreated,

    /// <summary>
    /// Organization details were updated.
    /// </summary>
    OrganizationUpdated,

    /// <summary>
    /// Organization was deactivated.
    /// </summary>
    OrganizationDeactivated,

    /// <summary>
    /// User was added to an organization.
    /// </summary>
    UserAddedToOrganization,

    /// <summary>
    /// User details or roles were updated.
    /// </summary>
    UserUpdatedInOrganization,

    /// <summary>
    /// User was removed from an organization.
    /// </summary>
    UserRemovedFromOrganization,

    /// <summary>
    /// Admin dashboard was accessed.
    /// </summary>
    AdminDashboardAccessed,

    /// <summary>
    /// Health dashboard was refreshed.
    /// </summary>
    HealthCheckRefreshed
}

/// <summary>
/// Result of an audit event query with pagination metadata.
/// </summary>
public record AuditQueryResult
{
    /// <summary>
    /// The audit events matching the query.
    /// </summary>
    public IReadOnlyList<AuditEventDto> Events { get; init; } = [];

    /// <summary>
    /// Total number of matching events.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; }
}

/// <summary>
/// Represents a single audit event.
/// </summary>
public record AuditEventDto
{
    /// <summary>
    /// Unique identifier for the audit event.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Type of audit event.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Identity ID of the user who triggered the event.
    /// </summary>
    public Guid? IdentityId { get; init; }

    /// <summary>
    /// IP address from which the event originated.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Whether the action succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Additional event-specific details.
    /// </summary>
    public Dictionary<string, object>? Details { get; init; }
}

/// <summary>
/// Audit log retention configuration.
/// </summary>
public record AuditRetentionDto
{
    /// <summary>
    /// Number of months to retain audit logs.
    /// </summary>
    public int RetentionMonths { get; init; }
}
