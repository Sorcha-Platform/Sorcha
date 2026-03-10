// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

using Fido2NetLib;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to generate passkey registration options.
/// </summary>
public record PasskeyRegisterOptionsRequest
{
    /// <summary>
    /// Human-readable name for this credential (e.g., "My YubiKey", "Work Laptop").
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }
}

/// <summary>
/// Response containing passkey registration challenge options.
/// </summary>
public record PasskeyRegistrationOptionsResponse
{
    /// <summary>
    /// Unique transaction ID to correlate with the verification step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// FIDO2 credential creation options to pass to the browser WebAuthn API.
    /// </summary>
    [JsonPropertyName("options")]
    public required object Options { get; init; }
}

/// <summary>
/// Request to verify a passkey registration attestation response.
/// </summary>
public record PasskeyRegisterVerifyRequest
{
    /// <summary>
    /// Transaction ID from the registration options step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// Raw attestation response from the browser/authenticator.
    /// </summary>
    [JsonPropertyName("attestation_response")]
    public required AuthenticatorAttestationRawResponse AttestationResponse { get; init; }
}

/// <summary>
/// Response representing a single passkey credential.
/// </summary>
public record PasskeyCredentialResponse
{
    /// <summary>
    /// Unique credential record ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>
    /// Human-readable name for this credential.
    /// </summary>
    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Authenticator device type (e.g., "YubiKey 5 NFC", "Windows Hello").
    /// </summary>
    [JsonPropertyName("device_type")]
    public string? DeviceType { get; init; }

    /// <summary>
    /// Current credential status (Active, Disabled, Revoked).
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Timestamp when the credential was registered.
    /// </summary>
    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp of the last successful authentication using this credential.
    /// </summary>
    [JsonPropertyName("last_used_at")]
    public DateTimeOffset? LastUsedAt { get; init; }
}

/// <summary>
/// Response containing a list of passkey credentials for the current user.
/// </summary>
public record PasskeyCredentialListResponse
{
    /// <summary>
    /// List of passkey credentials.
    /// </summary>
    [JsonPropertyName("credentials")]
    public required IReadOnlyList<PasskeyCredentialResponse> Credentials { get; init; }

    /// <summary>
    /// Maximum number of passkey credentials allowed per user.
    /// </summary>
    [JsonPropertyName("max_credentials")]
    public int MaxCredentials { get; init; } = 10;
}

/// <summary>
/// Request to verify a passkey assertion during 2FA login.
/// </summary>
public record PasskeyVerifyRequest
{
    /// <summary>
    /// Short-lived login token from the initial login response.
    /// </summary>
    [JsonPropertyName("login_token")]
    public required string LoginToken { get; init; }

    /// <summary>
    /// Transaction ID from the assertion options step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// Raw assertion response from the browser/authenticator.
    /// </summary>
    [JsonPropertyName("assertion_response")]
    public required AuthenticatorAssertionRawResponse AssertionResponse { get; init; }
}

/// <summary>
/// Request to get passkey assertion options for 2FA login.
/// </summary>
public record PasskeyAssertionOptionsRequest
{
    /// <summary>
    /// Short-lived login token from the initial login response.
    /// </summary>
    [JsonPropertyName("login_token")]
    public required string LoginToken { get; init; }
}

/// <summary>
/// Response containing passkey assertion challenge options for 2FA login.
/// </summary>
public record PasskeyAssertionOptionsResponse
{
    /// <summary>
    /// Unique transaction ID to correlate with the verification step.
    /// </summary>
    [JsonPropertyName("transaction_id")]
    public required string TransactionId { get; init; }

    /// <summary>
    /// FIDO2 assertion options to pass to the browser WebAuthn API.
    /// </summary>
    [JsonPropertyName("options")]
    public required object Options { get; init; }
}
