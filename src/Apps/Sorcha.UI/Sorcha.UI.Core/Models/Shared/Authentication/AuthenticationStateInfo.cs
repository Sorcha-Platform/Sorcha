// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Authentication state information for the current user
/// </summary>
public sealed record AuthenticationStateInfo
{
    /// <summary>
    /// Indicates if the user is authenticated
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Username or email of the authenticated user
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// User roles (e.g., Administrator, Designer, Viewer)
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Active profile name
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// Token expiration timestamp (UTC)
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Checks if the user has a specific role
    /// </summary>
    public bool HasRole(string role)
    {
        return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the user has any of the specified roles
    /// </summary>
    public bool HasAnyRole(params string[] roles)
    {
        return roles.Any(role => HasRole(role));
    }

    /// <summary>
    /// Creates an unauthenticated state
    /// </summary>
    public static AuthenticationStateInfo Unauthenticated()
    {
        return new AuthenticationStateInfo
        {
            IsAuthenticated = false
        };
    }
}
