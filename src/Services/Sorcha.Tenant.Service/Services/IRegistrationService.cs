// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a self-registration attempt.
/// </summary>
/// <param name="Success">Whether registration succeeded.</param>
/// <param name="UserId">The newly created user's ID (if successful).</param>
/// <param name="Message">Status message (e.g., "Verification email sent").</param>
/// <param name="ValidationErrors">Field-level validation errors (if any).</param>
/// <param name="Error">General error message (if failed).</param>
/// <param name="ErrorStatusCode">HTTP status code hint for the error (403, 409, etc.).</param>
public record RegistrationResult(
    bool Success,
    Guid? UserId = null,
    string? Message = null,
    Dictionary<string, string[]>? ValidationErrors = null,
    string? Error = null,
    int? ErrorStatusCode = null);

/// <summary>
/// Handles user self-registration with email/password for organizations
/// that have self-registration enabled. Validates password policy,
/// checks email uniqueness and domain restrictions, creates the user,
/// and sends verification email.
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Registers a new user in the specified organization.
    /// </summary>
    /// <param name="orgSubdomain">Organization subdomain to register under.</param>
    /// <param name="email">User's email address.</param>
    /// <param name="password">User's password (validated against NIST policy).</param>
    /// <param name="displayName">User's display name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Registration result with user ID or error details.</returns>
    Task<RegistrationResult> RegisterAsync(
        string orgSubdomain, string email, string password,
        string displayName, CancellationToken ct = default);
}
