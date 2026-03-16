// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Platform-level configuration flags (singleton entity).
/// Controls public organisation availability and org creation limits.
/// Stored in the public schema.
/// </summary>
public class PlatformSettings
{
    /// <summary>
    /// Unique identifier for this settings record.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Whether the public organisation is enabled for self-registration.
    /// When false, the public org is suspended and social/email signup is disabled.
    /// </summary>
    public bool PublicOrgEnabled { get; set; }

    /// <summary>
    /// Maximum number of private organisations a single user can create.
    /// Range: 1-100. Default: 1.
    /// </summary>
    public int MaxOrgsPerUser { get; set; } = 1;

    /// <summary>
    /// Timestamp of the last settings modification (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// ID of the system admin who last updated these settings.
    /// </summary>
    public Guid UpdatedBy { get; set; }
}
