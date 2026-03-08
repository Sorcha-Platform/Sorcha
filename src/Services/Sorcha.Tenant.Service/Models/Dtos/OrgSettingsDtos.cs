// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Response containing organization settings.
/// </summary>
public record OrgSettingsResponse
{
    /// <summary>
    /// Organization type (Standard or Public).
    /// </summary>
    public required string OrgType { get; init; }

    /// <summary>
    /// Whether self-registration is enabled for this organization.
    /// </summary>
    public bool SelfRegistrationEnabled { get; init; }

    /// <summary>
    /// Allowed email domains for auto-provisioning (empty = no restrictions).
    /// </summary>
    public string[] AllowedEmailDomains { get; init; } = [];

    /// <summary>
    /// Audit log retention period in months (1-120).
    /// </summary>
    public int AuditRetentionMonths { get; init; }
}

/// <summary>
/// Request to update organization settings.
/// </summary>
public record OrgSettingsRequest
{
    /// <summary>
    /// Enable or disable self-registration.
    /// </summary>
    public bool? SelfRegistrationEnabled { get; init; }

    /// <summary>
    /// Audit retention period in months (1-120).
    /// </summary>
    public int? AuditRetentionMonths { get; init; }
}
