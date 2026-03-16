// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Response containing current platform settings.
/// </summary>
public class PlatformSettingsResponse
{
    /// <summary>
    /// Whether the public organisation is enabled for self-registration.
    /// </summary>
    public bool PublicOrgEnabled { get; set; }

    /// <summary>
    /// Maximum number of private organisations a single user can create.
    /// </summary>
    public int MaxOrgsPerUser { get; set; }

    /// <summary>
    /// ID of the public organisation.
    /// </summary>
    public Guid PublicOrgId { get; set; }

    /// <summary>
    /// Current status of the public organisation.
    /// </summary>
    public string PublicOrgStatus { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the last settings modification (UTC).
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Request to enable or disable the public organisation.
/// </summary>
public class UpdatePublicOrgRequest
{
    /// <summary>
    /// Whether the public organisation should be enabled.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Request to update the maximum organisations per user setting.
/// </summary>
public class UpdateMaxOrgsRequest
{
    /// <summary>
    /// Maximum number of private organisations a single user can create. Range: 1-100.
    /// </summary>
    public int MaxOrgsPerUser { get; set; }
}
