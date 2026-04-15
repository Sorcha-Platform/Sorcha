// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Defines how a blueprint action mints a verifiable credential upon execution.
/// </summary>
public class CredentialIssuanceConfig
{
    /// <summary>
    /// Type of credential to issue (e.g., "LicenseCredential").
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("credentialType")]
    public string CredentialType { get; set; } = string.Empty;

    /// <summary>
    /// Maps action data fields to credential claims.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [JsonPropertyName("claimMappings")]
    public IEnumerable<ClaimMapping> ClaimMappings { get; set; } = [];

    /// <summary>
    /// Participant ID who receives the credential.
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("recipientParticipantId")]
    public string RecipientParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// How long the credential is valid (ISO 8601 duration, e.g., "P365D" = 1 year).
    /// Null means no expiry.
    /// </summary>
    [JsonPropertyName("expiryDuration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpiryDuration { get; set; }

    /// <summary>
    /// If set, records the credential on this register for public queryability
    /// (e.g., a "Register of Licenses").
    /// </summary>
    [DataAnnotations.MaxLength(100)]
    [JsonPropertyName("registerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegisterId { get; set; }

    /// <summary>
    /// Claim names that support selective disclosure. Null means all claims are disclosable.
    /// </summary>
    [JsonPropertyName("disclosable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? Disclosable { get; set; }

    /// <summary>
    /// Defines how many times the credential may be presented. Default: Reusable (unlimited).
    /// </summary>
    [JsonPropertyName("usagePolicy")]
    public UsagePolicy UsagePolicy { get; set; } = UsagePolicy.Reusable;

    /// <summary>
    /// Maximum number of presentations for LimitedUse credentials.
    /// Must be > 0 when UsagePolicy is LimitedUse; null otherwise.
    /// </summary>
    [JsonPropertyName("maxPresentations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxPresentations { get; set; }

    /// <summary>
    /// Issuer-defined visual template for how the credential appears in wallets.
    /// </summary>
    [JsonPropertyName("displayConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CredentialDisplayConfig? DisplayConfig { get; set; }

    /// <summary>
    /// Where the issued credential is delivered. Default: <see cref="TargetAudience.SorchaInternal"/>.
    /// Set to <see cref="TargetAudience.HaipExternalWallet"/> to issue via the HAIP OpenID4VCI
    /// path (spec 097) instead of writing to a Sorcha participant's wallet.
    /// </summary>
    [JsonPropertyName("targetAudience")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TargetAudience TargetAudience { get; set; } = TargetAudience.SorchaInternal;
}

/// <summary>
/// Controls how an issued credential is delivered to the recipient.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetAudience
{
    /// <summary>Internal Sorcha participant — credential written to Sorcha wallet.</summary>
    SorchaInternal = 0,

    /// <summary>External HAIP wallet — credential issued via OpenID4VCI pre-authorized code flow.</summary>
    HaipExternalWallet = 1,

    /// <summary>
    /// Register-native delivery to an on-platform Sorcha wallet (Feature 106). The engine mints
    /// an SD-JWT VC bound to the holder wallet's public key, encrypts it via
    /// <c>EncryptionPipelineService</c> (X25519 wrap + XChaCha20-Poly1305 AEAD), and seals it into
    /// the issuing action's transaction as a recipient-addressed disclosure. The credential
    /// peer-replicates through the register sync and is detected by the holder's Wallet Service
    /// regardless of whether the holder lives on the same node as the issuer.
    /// </summary>
    SorchaLocalWallet = 2
}
