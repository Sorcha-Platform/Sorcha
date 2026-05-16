// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Specifies what credential(s) a participant must present to execute a blueprint action.
/// </summary>
public class CredentialRequirement
{
    /// <summary>
    /// Credential type identifier (e.g., "LicenseCredential", "IdentityAttestation").
    /// </summary>
    [DataAnnotations.Required]
    [DataAnnotations.MinLength(1)]
    [DataAnnotations.MaxLength(200)]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// List of accepted issuer DIDs or wallet addresses. Empty means any issuer is accepted.
    /// </summary>
    [JsonPropertyName("acceptedIssuers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<string>? AcceptedIssuers { get; set; }

    /// <summary>
    /// Claims that must be disclosed and their value constraints.
    /// </summary>
    [JsonPropertyName("requiredClaims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IEnumerable<ClaimConstraint>? RequiredClaims { get; set; }

    /// <summary>
    /// Policy for handling revocation check failures. Defaults to FailClosed.
    /// </summary>
    [JsonPropertyName("revocationCheckPolicy")]
    public RevocationCheckPolicy RevocationCheckPolicy { get; set; } = RevocationCheckPolicy.FailClosed;

    /// <summary>
    /// Human-readable description of what credential is needed (displayed in UI).
    /// </summary>
    [DataAnnotations.MaxLength(500)]
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Where the presentation should come from. Default: <see cref="PresentationSource.SorchaInternal"/>.
    /// Set to <see cref="PresentationSource.HaipExternalWallet"/> to require presentation via
    /// the HAIP OpenID4VP verifier (spec 098) instead of matching against Sorcha-internal credentials.
    /// </summary>
    [JsonPropertyName("presentationSource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PresentationSource PresentationSource { get; set; } = PresentationSource.SorchaInternal;
}

/// <summary>
/// Controls where a credential presentation should originate from.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PresentationSource
{
    /// <summary>Internal Sorcha participant — matched against stored credentials.</summary>
    SorchaInternal = 0,

    /// <summary>External HAIP wallet — presented via OpenID4VP direct_post flow.</summary>
    HaipExternalWallet = 1,

    /// <summary>
    /// Sorcha Wallet PWA (Feature 114 +) — citizen presents from their on-device
    /// Sorcha wallet. Server-side validation runs in
    /// <c>SorchaWalletPresentationConsumer</c> via <c>Sorcha.Verifier.Engine</c>;
    /// no external verifier service is involved. Introduced by Feature 127 as
    /// the first non-HAIP consumer in the F111 timebound presentation lifecycle.
    /// </summary>
    SorchaWallet = 2
}
