// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Maps a custom domain to an organization for URL resolution.
/// Stored in public schema — accessed by API Gateway before authentication.
/// </summary>
public class CustomDomainMapping
{
    /// <summary>
    /// Unique mapping identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Organization this domain maps to. One custom domain per organization.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Custom domain name (e.g., "login.acmestores.com"). Globally unique.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// CNAME verification status.
    /// </summary>
    public CustomDomainStatus Status { get; set; } = CustomDomainStatus.Pending;

    /// <summary>
    /// Timestamp when the CNAME was last verified successfully.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>
    /// Timestamp of the last CNAME verification check (successful or failed).
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>
    /// Mapping creation timestamp (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
