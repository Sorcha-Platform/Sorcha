// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Links a social login provider to a PlatformUser.
/// Multiple providers can be linked per user (Google, GitHub, Microsoft, Apple).
/// Stored in the public schema for platform-wide uniqueness.
/// </summary>
public class PlatformSocialLogin
{
    /// <summary>
    /// Unique identifier for this social login link.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Foreign key to the owning PlatformUser.
    /// </summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>
    /// Social provider name (e.g., "google", "github", "microsoft", "apple").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Provider's unique user identifier (the "sub" claim from the provider).
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Email address from the social provider profile.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Display name from the social provider profile.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Timestamp when the social login was linked (UTC).
    /// </summary>
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last sign-in using this social login (UTC).
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Navigation property to the owning PlatformUser.
    /// </summary>
    public PlatformUser PlatformUser { get; set; } = null!;
}
