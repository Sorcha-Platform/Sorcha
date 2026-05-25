// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// An authorized validator's signing key declaration within a register's control record.
/// Each entry represents a single validator and its purpose-derived signing key.
/// </summary>
public class ValidatorRosterEntry
{
    /// <summary>
    /// Unique identifier for the validator (wallet address or DID).
    /// </summary>
    [Required]
    [StringLength(255, MinimumLength = 1)]
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded purpose-derived public key for docket signing.
    /// This is NOT the system wallet's master key — it is derived via the derivation context.
    /// </summary>
    [Required]
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Cryptographic algorithm used by this signing key.
    /// </summary>
    [Required]
    [JsonPropertyName("algorithm")]
    public SignatureAlgorithm Algorithm { get; set; }

    /// <summary>
    /// Derivation path or context used to derive this key from the system wallet
    /// (e.g., "sorcha:docket-signing"). Recorded for auditability.
    /// </summary>
    [Required]
    [JsonPropertyName("derivationContext")]
    public string DerivationContext { get; set; } = string.Empty;

    /// <summary>
    /// Current status of this validator key.
    /// </summary>
    [Required]
    [JsonPropertyName("status")]
    public ValidatorKeyStatus Status { get; set; } = ValidatorKeyStatus.Active;

    /// <summary>
    /// When this key was authorized for the register.
    /// </summary>
    [Required]
    [JsonPropertyName("authorizedAt")]
    public DateTimeOffset AuthorizedAt { get; set; }

    /// <summary>
    /// When this key was rotated or revoked. Null if still Active.
    /// </summary>
    [JsonPropertyName("revokedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Feature 138 US3 — transaction id of the sealing <c>control.validator.eject</c> or
    /// <c>control.validator.liveness-violation</c> that moved this entry to
    /// <see cref="ValidatorKeyStatus.Ejected"/>. Null unless ejected. Lets any node trace the
    /// ejection back to its on-chain evidence.
    /// </summary>
    [JsonPropertyName("ejectionRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EjectionRef { get; set; }

    /// <summary>
    /// Feature 138 US3 — why this validator was ejected (<c>Equivocation</c> or
    /// <c>LivenessTimeout</c>). Null unless ejected.
    /// </summary>
    [JsonPropertyName("ejectionReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidatorEjectionReason? EjectionReason { get; set; }
}

/// <summary>
/// Reason a validator was automatically ejected from the sealed roster (Feature 138 US3).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidatorEjectionReason
{
    /// <summary>Signed two conflicting states for the same slot (double-vote / equivocation).</summary>
    Equivocation,

    /// <summary>Accepted work for sealing but did not seal within the liveness window.</summary>
    LivenessTimeout
}
