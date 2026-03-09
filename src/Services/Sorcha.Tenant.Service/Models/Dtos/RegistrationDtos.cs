// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to self-register a local account with email/password.
/// Only available for public orgs with self-registration enabled.
/// </summary>
public record SelfRegistrationRequest
{
    /// <summary>
    /// Organization subdomain to register under.
    /// </summary>
    public required string OrgSubdomain { get; init; }

    /// <summary>
    /// User's email address. Must be unique within the organization.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Password (min 12 characters, checked against HIBP breach list).
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public required string DisplayName { get; init; }
}

/// <summary>
/// Response after successful self-registration.
/// </summary>
public record SelfRegistrationResponse
{
    /// <summary>
    /// Whether registration was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The newly created user's ID.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Status message (e.g., "Verification email sent").
    /// </summary>
    public string? Message { get; init; }
}
