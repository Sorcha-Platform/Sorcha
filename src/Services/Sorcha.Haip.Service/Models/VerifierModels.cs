// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.Haip.Service.Models;

/// <summary>
/// Represents a pending presentation request stored in Redis.
/// Created when a Blueprint action requires an external HAIP wallet presentation.
/// </summary>
public class PresentationRequest
{
    /// <summary>Unique identifier for the resource.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The nonce.</summary>
    public required string Nonce { get; set; }
    /// <summary>Identifier of the client.</summary>
    public required string ClientId { get; set; }
    /// <summary>The response uri.</summary>
    public required string ResponseUri { get; set; }
    /// <summary>The credential type (single-ask convenience — first credential query's vct).</summary>
    public required string CredentialType { get; set; }
    /// <summary>Collection of required claims associated with this resource.</summary>
    public List<string>? RequiredClaims { get; set; }

    /// <summary>
    /// Feature 181 US2 — the full declared DCQL query when the request asks for more than one
    /// credential (or carries <c>credential_sets</c> alternatives). Null ⇒ the single-ask request
    /// built from <see cref="CredentialType"/> + <see cref="RequiredClaims"/> (back-compatible).
    /// </summary>
    public DcqlQuery? DeclaredQuery { get; set; }
    /// <summary>Collection of accepted issuers associated with this resource.</summary>
    public List<string>? AcceptedIssuers { get; set; }
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Timestamp at which the record expires (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Current state of the resource.</summary>
    public PresentationRequestState State { get; set; } = PresentationRequestState.Pending;

    /// <summary>
    /// Opaque state token for OID4VP state correlation (CSRF protection).
    /// Must be set explicitly by the store on creation — not auto-initialized.
    /// </summary>
    public string StateToken { get; set; } = string.Empty;

    /// <summary>The result.</summary>
    public VerificationResult? Result { get; set; }

    /// <summary>
    /// The raw <c>vp_token</c> string the holder submitted via <c>direct_post</c>, retained so a
    /// verifier client can re-validate the presentation locally and build its own rich verdict
    /// (Verify-unification PR B1). Null until a holder has submitted; shares the request's TTL.
    /// </summary>
    public string? SubmittedVpToken { get; set; }

    /// <summary>
    /// The OID4VP <c>presentation_submission</c> the holder submitted alongside the
    /// <see cref="SubmittedVpToken"/>, when present. Null until submission.
    /// </summary>
    public string? PresentationSubmission { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PresentationRequestState
{
    Pending = 0,
    Submitted = 1,
    Verified = 2,
    Denied = 3,
    Expired = 4,
    Cancelled = 10 // Non-contiguous to avoid shifting Expired's ordinal (existing Redis data safety)
}

/// <summary>
/// Result of verifying a HAIP presentation submission.
/// </summary>
public class VerificationResult
{
    /// <summary>Indicates whether validation passed (overall: every required query / credential-set satisfied).</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>
    /// Feature 181 US2 — per-credential-query verification outcomes, keyed by DCQL query id. Populated
    /// for multi-query requests; null for the single-ask flow. The overall <see cref="IsValid"/> applies
    /// the request's <c>credential_sets</c> (or AND-of-all) rule over these results.
    /// </summary>
    [JsonPropertyName("perQuery")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, PerQueryVerification>? PerQuery { get; set; }

    // TODO(098-#45): Key by input descriptor ID: Dictionary<string, Dictionary<string, object>>
    /// <summary>Map of verified claims keyed by string.</summary>
    [JsonPropertyName("verifiedClaims")]
    public Dictionary<string, object> VerifiedClaims { get; set; } = new();

    // TODO(098-#44): Migrate to structured VerificationError with Kind, Description, InputDescriptorId
    /// <summary>Collection of error details when the operation did not succeed.</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    /// <summary>Flag indicating holder key verified.</summary>
    [JsonPropertyName("holderKeyVerified")]
    public bool HolderKeyVerified { get; set; }

    /// <summary>Flag indicating x5c chain valid.</summary>
    [JsonPropertyName("x5cChainValid")]
    public bool? X5cChainValid { get; set; }

    /// <summary>The status check result.</summary>
    [JsonPropertyName("statusCheckResult")]
    public string? StatusCheckResult { get; set; }

    /// <summary>The issuer.</summary>
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    /// <summary>
    /// Feature 135 (T033) — the pinnable trust evidence produced by the unified
    /// <see cref="Sorcha.Blueprint.Engine.Credentials.ITrustEvaluator"/>: which source vouched,
    /// the assurance established, and the policy digest. Carried onto spec-079 verification
    /// receipts so a decision can be re-checked offline (FR-014/FR-015). Null until the trust
    /// evaluator has run.
    /// </summary>
    [JsonPropertyName("trustEvidence")]
    public Sorcha.Blueprint.Engine.Credentials.TrustEvidence? TrustEvidence { get; set; }
}

/// <summary>Feature 181 US2 — the verification outcome for a single DCQL credential query.</summary>
public sealed class PerQueryVerification
{
    /// <summary>Whether the presentation for this query verified.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>The verified issuer for this query, when the presentation verified.</summary>
    [JsonPropertyName("issuer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Issuer { get; set; }

    /// <summary>Errors for this query when it did not verify (empty when valid).</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}
