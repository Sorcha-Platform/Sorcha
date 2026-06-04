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
    /// Where the issued credential is delivered. Default: <see cref="TargetAudience.SorchaLocalWallet"/>.
    /// Set to <see cref="TargetAudience.HaipExternalWallet"/> to issue via the HAIP OpenID4VCI
    /// path (spec 097) instead of writing to a Sorcha participant's wallet.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="TargetAudience.SorchaLocalWallet"/> (was the deprecated
    /// <see cref="TargetAudience.SorchaInternal"/>). The runtime already maps SorchaInternal to
    /// SorchaLocalWallet, so configs that omit this field — previously defaulting to the
    /// deprecated value — keep identical delivery behaviour; they just resolve to the
    /// non-deprecated value directly.
    /// </remarks>
    [JsonPropertyName("targetAudience")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TargetAudience TargetAudience { get; set; } = TargetAudience.SorchaLocalWallet;

    /// <summary>
    /// Credential format to mint (feature 135). Default <see cref="CredentialFormat.SdJwtVc"/>.
    /// </summary>
    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public CredentialFormat Format { get; set; } = CredentialFormat.SdJwtVc;

    /// <summary>
    /// Trust anchor the issued credential is trusted under (feature 135). Default
    /// <see cref="TrustAnchor.Register"/>. X.509 anchors attach the org certificate chain;
    /// the register anchor relies on decentralised-identifier resolution.
    /// </summary>
    [JsonPropertyName("trustAnchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public TrustAnchor TrustAnchor { get; set; } = TrustAnchor.Register;

    /// <summary>
    /// Feature 137 (cross-node submission). JSON Pointer into the reconstructed instance state
    /// where the recipient's carried delivery keys live — written by a <c>sorcha-holder-key</c>
    /// form field on a starting action (default <c>/holderKeys/holderJwk</c>; siblings
    /// <c>/holderKeys/encryptionPublicKey</c> + <c>/holderKeys/algorithm</c> are derived from the
    /// same parent object). When set, the issuer binds the credential to the carried holder JWK
    /// (SD-JWT <c>cnf</c>) and, for an open-participant recipient with no published participant
    /// record (cross-node late binding), wraps the on-register AEAD envelope to the carried
    /// encryption key. Resolution precedence is published participant record → carried keys →
    /// fail closed without issuing (FR-012). Null (the default) preserves the pre-137 behaviour:
    /// no <c>cnf</c> binding and no carried-key fallback.
    /// </summary>
    [JsonPropertyName("holderKeySourceField")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HolderKeySourceField { get; set; }
}

/// <summary>
/// Controls how an issued credential is delivered to the recipient.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetAudience
{
    /// <summary>
    /// Deprecated: maps to <see cref="SorchaLocalWallet"/> at runtime. Originally wrote
    /// the credential directly to the recipient's wallet on the same node, bypassing the
    /// register — which breaks on multi-node deployments. Use <see cref="SorchaLocalWallet"/>
    /// for all on-platform credential delivery.
    /// </summary>
    [Obsolete("Use SorchaLocalWallet instead. SorchaInternal bypasses the register and breaks on multi-node.")]
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
