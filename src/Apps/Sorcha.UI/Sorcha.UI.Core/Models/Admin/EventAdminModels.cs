// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model representing a system event in the admin interface.
/// </summary>
public record SystemEventViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string? UserId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Filter model for querying system events.
/// </summary>
public record EventFilterModel
{
    public string? Severity { get; init; }
    public DateTime? Since { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Paginated response containing system events.
/// </summary>
public record EventListResponse
{
    public List<SystemEventViewModel> Events { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
