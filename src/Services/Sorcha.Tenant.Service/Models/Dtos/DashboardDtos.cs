// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Admin dashboard statistics response.
/// </summary>
public record DashboardResponse
{
    /// <summary>
    /// Number of active users in the organization.
    /// </summary>
    public int ActiveUserCount { get; init; }

    /// <summary>
    /// Number of suspended users in the organization.
    /// </summary>
    public int SuspendedUserCount { get; init; }

    /// <summary>
    /// User count breakdown by role.
    /// </summary>
    public Dictionary<string, int> UsersByRole { get; init; } = new();

    /// <summary>
    /// Most recent logins (last 10).
    /// </summary>
    public List<RecentLoginEntry> RecentLogins { get; init; } = [];

    /// <summary>
    /// Number of pending (unsent/unaccepted) invitations.
    /// </summary>
    public int PendingInvitationCount { get; init; }

    /// <summary>
    /// External IDP configuration status.
    /// </summary>
    public IdpStatusInfo IdpStatus { get; init; } = new();
}

/// <summary>
/// A recent login entry for the dashboard.
/// </summary>
public record RecentLoginEntry
{
    /// <summary>
    /// User who logged in.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// When the login occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Login method used (Local, OIDC, PassKey).
    /// </summary>
    public string Method { get; init; } = string.Empty;
}

/// <summary>
/// IDP configuration status for the dashboard.
/// </summary>
public record IdpStatusInfo
{
    /// <summary>
    /// Whether an IDP is configured for this organization.
    /// </summary>
    public bool Configured { get; init; }

    /// <summary>
    /// Whether the configured IDP is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Name of the configured IDP provider.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Timestamp of the most recent login via the external IDP.
    /// </summary>
    public DateTimeOffset? LastLoginViaIdp { get; init; }
}
