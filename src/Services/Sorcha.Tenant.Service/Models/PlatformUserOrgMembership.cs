// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Denormalized lookup for the org switcher. Maps a PlatformUser to an Organization membership.
/// Avoids cross-schema queries when listing a user's organisations.
/// Stored in the public schema for platform-wide access.
/// </summary>
public class PlatformUserOrgMembership
{
    /// <summary>
    /// Unique identifier for this membership record.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the PlatformUser.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Foreign key to the Organization.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Highest role the user holds in this organisation (denormalized from UserIdentity).
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the user joined the organisation (UTC).
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Navigation property to the PlatformUser.
    /// </summary>
    public PlatformUser PlatformUser { get; set; } = null!;

    /// <summary>
    /// Navigation property to the Organization.
    /// </summary>
    public Organization Organization { get; set; } = null!;
}
