// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// OAuth2 token response from Tenant Service
/// </summary>
public sealed record TokenResponse
{
    /// <summary>
    /// JWT access token
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// JWT refresh token (optional)
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Token type (always "Bearer")
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Token expiration in seconds
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// Validates the token response
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(AccessToken) &&
               ExpiresIn > 0;
    }
}
