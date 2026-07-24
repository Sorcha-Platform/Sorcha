// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Represents a user in the Sorcha platform.
/// </summary>
public class User
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Organization ID the user belongs to.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// Username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// First name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// Whether the user is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// User roles.
    /// </summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Request to create a new user.
/// </summary>
public class CreateUserRequest
{
    /// <summary>
    /// Username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// First name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Last name.
    /// </summary>
    public string? LastName { get; set; }

    /// <summary>
    /// User roles.
    /// </summary>
    public List<string>? Roles { get; set; }
}

/// <summary>
/// Request to update a user.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.UpdateUserRequest</c> exactly; the pairing is
/// asserted by <c>CliWireContractTests</c>. This previously sent <c>email</c>, <c>firstName</c>,
/// <c>lastName</c> and <c>isActive</c>, none of which the server binds — so <c>sorcha user update</c>
/// posted a body the server ignored entirely, then reported success having changed nothing.
/// </remarks>
public class UpdateUserRequest
{
    /// <summary>
    /// The user's display name. Replaces the separate first/last name fields the CLI used to send;
    /// the server has only ever had a single display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Replacement role set. Serialised as strings; the server binds them to its UserRole enum.
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    /// Account status, e.g. <c>Active</c> or <c>Suspended</c>. Replaces the CLI's boolean
    /// <c>isActive</c>, which the server never accepted.
    /// </summary>
    public string? Status { get; set; }
}
