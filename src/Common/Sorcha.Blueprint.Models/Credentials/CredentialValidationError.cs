// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Describes a specific credential verification failure.
/// </summary>
public class CredentialValidationError
{
    /// <summary>
    /// Which credential type requirement failed.
    /// </summary>
    [JsonPropertyName("requirementType")]
    public string RequirementType { get; set; } = string.Empty;

    /// <summary>
    /// The specific reason the credential verification failed.
    /// </summary>
    [JsonPropertyName("failureReason")]
    public CredentialFailureReason FailureReason { get; set; }

    /// <summary>
    /// Human-readable error message with details about the failure.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Enumeration of credential verification failure reasons.
/// </summary>
public enum CredentialFailureReason
{
    /// <summary>No credential was presented for the requirement.</summary>
    Missing,

    /// <summary>The credential has expired.</summary>
    Expired,

    /// <summary>The credential has been revoked by the issuer. Terminal — it cannot be reinstated.</summary>
    Revoked,

    /// <summary>
    /// The credential has been suspended by the issuer. Refused like a revocation, but the issuer
    /// may lift it — so holder-facing copy must not use the terminal wording.
    /// </summary>
    Suspended,

    /// <summary>The credential's cryptographic signature is invalid.</summary>
    InvalidSignature,

    /// <summary>The credential's issuer is not in the accepted issuers list.</summary>
    IssuerNotAccepted,

    /// <summary>A required claim value does not match the constraint.</summary>
    ClaimMismatch,

    /// <summary>The revocation registry is unreachable and the policy is fail-closed.</summary>
    RevocationCheckUnavailable
}
