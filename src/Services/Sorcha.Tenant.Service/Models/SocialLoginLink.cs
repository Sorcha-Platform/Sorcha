// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Links a PublicIdentity to an external social login provider (Google, Microsoft, GitHub, Apple).
/// Enables passwordless sign-in via OAuth2/OIDC social providers.
/// </summary>
public class SocialLoginLink
{
    /// <summary>
    /// Unique identifier for this social login link.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the owning PublicIdentity.
    /// </summary>
    public Guid PublicIdentityId { get; set; }

    /// <summary>
    /// Social login provider type (e.g., "Google", "Microsoft", "GitHub", "Apple").
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// Provider's unique user identifier (the "sub" claim from the provider's ID token).
    /// </summary>
    public string ExternalSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Email address associated with the social account. May differ from the PublicIdentity email.
    /// </summary>
    public string? LinkedEmail { get; set; }

    /// <summary>
    /// Display name from the social provider profile.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Timestamp when the social login link was created (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last sign-in using this social login link (UTC).
    /// Null if never used after linking.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Navigation property to the owning PublicIdentity.
    /// </summary>
    public PublicIdentity? PublicIdentity { get; set; }
}
