// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Verifiable credential summary.
/// </summary>
public class CredentialSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Full verifiable credential detail.
/// </summary>
public class CredentialDetail
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("claims")]
    public Dictionary<string, string> Claims { get; set; } = new();

    [JsonPropertyName("proof")]
    public string Proof { get; set; } = string.Empty;
}

/// <summary>
/// Request to issue a verifiable credential.
/// </summary>
/// <remarks>
/// Mirrors the required surface of <c>Sorcha.Wallet.Service</c>'s <c>IssueCredentialRequest</c> for
/// <c>POST /api/v1/credentials/issue</c>. The previous shape (<c>type</c>/<c>subject</c>/
/// <c>walletAddress</c>/<c>expiresInDays</c>) matched no server field, AND the command posted to
/// <c>POST /api/v1/credentials</c> — which is <c>StoreCredential</c>, not issuance — so the command
/// could never issue anything. Claims are objects, not strings, on the wire; the recipient is a
/// wallet address, not a "subject". The many issuer-infrastructure fields (holderJwk, tenantId,
/// trustAnchor, status-list wiring) are optional server-side and left to the server's defaults —
/// blueprint-action-driven issuance remains the richer path; this covers the direct case.
/// </remarks>
public class IssueCredentialRequest
{
    /// <summary>The credential type, e.g. <c>IdentityCredential</c>.</summary>
    [JsonPropertyName("credentialType")]
    public string CredentialType { get; set; } = string.Empty;

    /// <summary>The claims to embed. Values are arbitrary JSON, not just strings.</summary>
    [JsonPropertyName("claims")]
    public Dictionary<string, object> Claims { get; set; } = new();

    /// <summary>The recipient's wallet address.</summary>
    [JsonPropertyName("recipientWallet")]
    public string RecipientWallet { get; set; } = string.Empty;

    /// <summary>Optional display name for the credential.</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional ISO-8601 duration until expiry, e.g. <c>P5Y</c>. Null issues a non-expiring
    /// credential (subject to server policy).
    /// </summary>
    [JsonPropertyName("expiryDuration")]
    public string? ExpiryDuration { get; set; }

    /// <summary>Optional list of claim names the holder may selectively disclose.</summary>
    [JsonPropertyName("disclosableClaims")]
    public List<string>? DisclosableClaims { get; set; }
}

/// <summary>
/// Response from issuing a verifiable credential — mirrors <c>Sorcha.Wallet.Service</c>'s
/// <c>IssuedCredentialResponse</c> (what <c>POST /api/v1/credentials/issue</c> produces).
/// </summary>
public class IssuedCredentialResponse
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("issuerDid")]
    public string IssuerDid { get; set; } = string.Empty;

    [JsonPropertyName("subjectDid")]
    public string SubjectDid { get; set; } = string.Empty;

    /// <summary>The issued claims. Values are arbitrary JSON.</summary>
    [JsonPropertyName("claims")]
    public Dictionary<string, object> Claims { get; set; } = new();

    [JsonPropertyName("issuedAt")]
    public DateTimeOffset IssuedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("rawToken")]
    public string RawToken { get; set; } = string.Empty;

    /// <summary>Optional display configuration JSON for the credential.</summary>
    [JsonPropertyName("displayConfigJson")]
    public string? DisplayConfigJson { get; set; }
}

/// <summary>
/// Request to present a verifiable credential.
/// </summary>
public class PresentCredentialRequest
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("verifierAddress")]
    public string VerifierAddress { get; set; } = string.Empty;

    [JsonPropertyName("selectedClaims")]
    public List<string>? SelectedClaims { get; set; }
}

/// <summary>
/// Response from credential presentation.
/// </summary>
public class PresentCredentialResponse
{
    [JsonPropertyName("presentationId")]
    public string PresentationId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("verifiedAt")]
    public DateTimeOffset? VerifiedAt { get; set; }
}

/// <summary>
/// Request to verify a credential.
/// </summary>
public class VerifyCredentialRequest
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;
}

/// <summary>
/// Response from credential verification.
/// </summary>
public class VerifyCredentialResponse
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("verifiedAt")]
    public DateTimeOffset VerifiedAt { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Request body for credential suspend/reinstate operations.
/// </summary>
public class LifecycleCredentialRequest
{
    [JsonPropertyName("issuerWallet")]
    public string IssuerWallet { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Request body for credential refresh operations.
/// </summary>
public class RefreshCredentialRequest
{
    [JsonPropertyName("issuerWallet")]
    public string IssuerWallet { get; set; } = string.Empty;

    [JsonPropertyName("newExpiryDuration")]
    public string? NewExpiryDuration { get; set; }
}

/// <summary>
/// Response from credential lifecycle operations (suspend/reinstate/revoke).
/// </summary>
public class CredentialLifecycleResponse
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("newStatus")]
    public string NewStatus { get; set; } = string.Empty;

    [JsonPropertyName("performedBy")]
    public string PerformedBy { get; set; } = string.Empty;

    [JsonPropertyName("performedAt")]
    public DateTimeOffset PerformedAt { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Response from credential refresh operations.
/// </summary>
public class RefreshCredentialResponse
{
    [JsonPropertyName("oldCredentialId")]
    public string OldCredentialId { get; set; } = string.Empty;

    [JsonPropertyName("newCredentialId")]
    public string NewCredentialId { get; set; } = string.Empty;

    [JsonPropertyName("newExpiresAt")]
    public DateTimeOffset NewExpiresAt { get; set; }
}

/// <summary>
/// W3C Bitstring Status List response.
/// </summary>
public class StatusListResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("purpose")]
    public string Purpose { get; set; } = string.Empty;

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("validFrom")]
    public DateTimeOffset ValidFrom { get; set; }

    [JsonPropertyName("encodedList")]
    public string EncodedList { get; set; } = string.Empty;

    [JsonPropertyName("@context")]
    public string[] ContextUrls { get; set; } = [];
}

/// <summary>
/// Credential status response.
/// </summary>
public class CredentialStatusResponse
{
    [JsonPropertyName("credentialId")]
    public string CredentialId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; set; }

    [JsonPropertyName("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonPropertyName("revokedReason")]
    public string? RevokedReason { get; set; }
}
