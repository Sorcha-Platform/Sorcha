// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to initiate a social login flow.
/// </summary>
public class SocialLoginInitiateRequest
{
    /// <summary>
    /// Social provider name (google, github, microsoft, apple).
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL to redirect to after successful authentication.
    /// </summary>
    [JsonPropertyName("returnUrl")]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Flow intent: <c>"login"</c> (default — anonymous flow that resolves
    /// or creates a PlatformUser) or <c>"link"</c> (signed-in flow that
    /// adds the social provider to the caller's existing PlatformUser).
    /// Feature 116 / Q6.
    /// </summary>
    [JsonPropertyName("intent")]
    public string? Intent { get; set; }

    /// <summary>
    /// Optional return-surface for the post-OAuth redirect: <c>wallet</c> routes
    /// the callback back into the citizen wallet PWA (and mints Consumer-tier);
    /// null/anything else keeps the default /app web flow. Spec: PWA social sign-in.
    /// </summary>
    [JsonPropertyName("surface")]
    public string? Surface { get; init; }
}

/// <summary>
/// Response from initiating a social login flow.
/// </summary>
public class SocialLoginInitiateResponse
{
    /// <summary>
    /// Authorization URL to redirect the user to the social provider.
    /// </summary>
    [JsonPropertyName("authorizationUrl")]
    public string AuthorizationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Opaque state parameter for CSRF protection.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// Request to complete a social login with the authorization code.
/// </summary>
public class SocialLoginCallbackRequest
{
    /// <summary>
    /// Authorization code from the social provider callback.
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// State parameter from the social provider callback.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Social provider name (must match the provider used in initiate).
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;
}
