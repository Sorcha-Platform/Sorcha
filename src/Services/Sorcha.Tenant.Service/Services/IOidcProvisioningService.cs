// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Provisions or matches users after successful OIDC authentication.
/// Handles claim mapping, auto-provisioning with Member role, domain restrictions,
/// and profile completion determination.
/// </summary>
public interface IOidcProvisioningService
{
    /// <summary>
    /// Matches an existing user by ExternalIdpSubject or auto-provisions a new user.
    /// New users receive Member role and ProvisionedVia=Oidc.
    /// Returning users get their LastLoginAt updated.
    /// </summary>
    /// <param name="orgId">Organization to provision the user in.</param>
    /// <param name="claims">Extracted OIDC claims from the ID token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of the matched/provisioned user and whether this was a first login.</returns>
    Task<(UserIdentity User, bool IsFirstLogin)> ProvisionOrMatchUserAsync(
        Guid orgId, OidcUserClaims claims, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether the user's email domain is allowed by the organization's domain restrictions.
    /// Returns true if AllowedEmailDomains is empty (no restrictions) or the email domain matches.
    /// </summary>
    /// <param name="orgId">Organization to check restrictions for.</param>
    /// <param name="email">Email address to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the email domain is allowed, false otherwise.</returns>
    Task<bool> CheckDomainRestrictionsAsync(
        Guid orgId, string email, CancellationToken cancellationToken);

    /// <summary>
    /// Determines whether a user's profile needs completion (missing email or display name).
    /// </summary>
    /// <param name="user">User to check.</param>
    /// <returns>True if profile is incomplete and needs user input.</returns>
    Task<bool> DetermineProfileCompletionAsync(UserIdentity user);
}
