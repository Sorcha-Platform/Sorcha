// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Result of matching a wallet's credentials against an action requirement.
/// </summary>
public class CredentialMatchResult
{
    /// <summary>
    /// The credential type that was matched against.
    /// </summary>
    [JsonPropertyName("requirementType")]
    public string RequirementType { get; set; } = string.Empty;

    /// <summary>
    /// Whether a matching credential was found in the wallet.
    /// </summary>
    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    /// <summary>
    /// ID of the matched credential (null if no match).
    /// </summary>
    [JsonPropertyName("credentialId")]
    public string? CredentialId { get; set; }

    /// <summary>
    /// Issuer DID of the matched credential.
    /// </summary>
    [JsonPropertyName("issuerDid")]
    public string? IssuerDid { get; set; }

    /// <summary>
    /// Expiration date of the matched credential.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }
}
