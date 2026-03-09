// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to configure a custom domain for an organization.
/// </summary>
public record ConfigureCustomDomainRequest
{
    /// <summary>
    /// The custom domain name (e.g., "login.acmestores.com").
    /// </summary>
    [Required]
    [RegularExpression(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?)*\.[a-zA-Z]{2,}$",
        ErrorMessage = "Invalid domain name format.")]
    public required string Domain { get; init; }
}

/// <summary>
/// Response with current custom domain configuration and status.
/// </summary>
public record CustomDomainResponse
{
    /// <summary>
    /// The configured custom domain name.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>
    /// Current verification status (None, Pending, Verified, Failed).
    /// </summary>
    public string Status { get; init; } = "None";

    /// <summary>
    /// When the domain was last verified successfully.
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; init; }

    /// <summary>
    /// The CNAME target that the domain should point to.
    /// </summary>
    public string? CnameTarget { get; init; }
}

/// <summary>
/// Response with CNAME configuration instructions after setting up a custom domain.
/// </summary>
public record CnameInstructionsResponse
{
    /// <summary>
    /// The custom domain being configured.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// The CNAME record target (e.g., "{subdomain}.sorcha.io").
    /// </summary>
    public required string CnameTarget { get; init; }

    /// <summary>
    /// Human-readable setup instructions.
    /// </summary>
    public required string Instructions { get; init; }
}

/// <summary>
/// Response from a domain verification check.
/// </summary>
public record DomainVerificationResponse
{
    /// <summary>
    /// Whether the CNAME record was verified successfully.
    /// </summary>
    public bool Verified { get; init; }

    /// <summary>
    /// Human-readable verification result message.
    /// </summary>
    public required string Message { get; init; }
}
