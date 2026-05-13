// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Result of a login attempt that may require two-factor authentication.
/// </summary>
public sealed record LoginResult
{
    /// <summary>
    /// Whether the login succeeded with tokens (no 2FA required).
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Whether two-factor authentication is required to complete login.
    /// </summary>
    public bool RequiresTwoFactor { get; init; }

    /// <summary>
    /// Token response when login succeeded without 2FA.
    /// </summary>
    public TokenResponse? TokenResponse { get; init; }

    /// <summary>
    /// Short-lived login token for the 2FA verification step.
    /// Only set when <see cref="RequiresTwoFactor"/> is true.
    /// </summary>
    public string? LoginToken { get; init; }

    /// <summary>
    /// Available two-factor authentication methods (e.g., "totp", "passkey").
    /// Only set when <see cref="RequiresTwoFactor"/> is true.
    /// </summary>
    public string[] AvailableMethods { get; init; } = [];

    /// <summary>
    /// Human-readable message from the server.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Creates a successful login result with tokens.
    /// </summary>
    public static LoginResult Success(TokenResponse tokenResponse) => new()
    {
        Succeeded = true,
        TokenResponse = tokenResponse
    };

    /// <summary>
    /// Creates a result indicating two-factor authentication is required.
    /// </summary>
    public static LoginResult TwoFactorRequired(string loginToken, string[] availableMethods, string? message = null) => new()
    {
        RequiresTwoFactor = true,
        LoginToken = loginToken,
        AvailableMethods = availableMethods,
        Message = message
    };
}

/// <summary>
/// DTO for deserializing two-factor login responses from the API.
/// </summary>
internal sealed record TwoFactorLoginResponseDto
{
    /// <summary>
    /// Always true when this response is returned.
    /// </summary>
    [JsonPropertyName("requires_two_factor")]
    public bool RequiresTwoFactor { get; init; }

    /// <summary>
    /// Short-lived login token for the 2FA verification step.
    /// </summary>
    [JsonPropertyName("login_token")]
    public string LoginToken { get; init; } = string.Empty;

    /// <summary>
    /// Available two-factor authentication methods.
    /// </summary>
    [JsonPropertyName("available_methods")]
    public string[] AvailableMethods { get; init; } = ["totp"];

    /// <summary>
    /// Human-readable message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
