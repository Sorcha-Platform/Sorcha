// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Request payload for OAuth2 Password Grant authentication
/// </summary>
public sealed record LoginRequest
{
    /// <summary>
    /// Username or email address
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// User password
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// OAuth2 grant type (always "password")
    /// </summary>
    public string GrantType { get; init; } = "password";

    /// <summary>
    /// Validates the login request
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Username) &&
               Username.Length >= 3 &&
               Username.Length <= 256 &&
               !string.IsNullOrWhiteSpace(Password) &&
               Password.Length >= 8 &&
               Password.Length <= 128;
    }
}
