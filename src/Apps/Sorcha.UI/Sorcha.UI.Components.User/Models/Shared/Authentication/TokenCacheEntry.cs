// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Cached token entry stored in encrypted LocalStorage
/// </summary>
public sealed record TokenCacheEntry
{
    /// <summary>
    /// JWT access token
    /// </summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// JWT refresh token (optional)
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Token expiration timestamp (UTC)
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Profile name this token belongs to
    /// </summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>
    /// Token issuance timestamp (UTC)
    /// </summary>
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Checks if the token is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>
    /// Checks if the token is near expiration (within 5 minutes)
    /// </summary>
    public bool IsNearExpiration => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);

    /// <summary>
    /// Gets the remaining token lifetime
    /// </summary>
    public TimeSpan TimeRemaining => ExpiresAt - DateTime.UtcNow;

    /// <summary>
    /// Creates a TokenCacheEntry from a TokenResponse
    /// </summary>
    public static TokenCacheEntry FromTokenResponse(TokenResponse response, string profileName)
    {
        return new TokenCacheEntry
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(response.ExpiresIn),
            ProfileName = profileName,
            IssuedAt = DateTime.UtcNow
        };
    }
}
