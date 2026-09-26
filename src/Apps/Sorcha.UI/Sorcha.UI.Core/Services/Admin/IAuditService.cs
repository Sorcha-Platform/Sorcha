// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side audit service that READS the organisation's audit log from the Tenant Service.
/// </summary>
/// <remarks>
/// This service has no write path. Org/user mutation audit entries (organization create/update/
/// deactivate, user add/update/remove) are written server-side, inside the Tenant Service endpoint
/// that performs the mutation (<c>OrganizationEndpoints</c>), on the success path only — never by
/// a client-side POST. That used to go through <c>LogAsync</c> / <c>LogOrganizationEventAsync</c> /
/// <c>LogUserEventAsync</c> here, posting to <c>/api/audit</c> — a route no service ever mapped, so
/// every one of those six events was silently lost, and a client-authored trail can be skipped or
/// forged regardless (#1655). Those methods are deleted; do not reintroduce a client-side write path.
/// </remarks>
public interface IAuditService
{
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
