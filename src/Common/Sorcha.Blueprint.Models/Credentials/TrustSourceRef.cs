// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// One trust source within a <see cref="TrustPolicy"/> (feature 135). Names the
/// <see cref="TrustSourceKind"/> to consult and any source-specific configuration.
/// </summary>
public class TrustSourceRef
{
    /// <summary>Which kind of trust source to consult.</summary>
    [JsonPropertyName("kind")]
    public TrustSourceKind Kind { get; set; }

    /// <summary>
    /// Assurance level this source confers when it vouches. Null is treated as
    /// <see cref="AssuranceLevel.Low"/>. An explicit credential assurance claim may
    /// raise (never lower) the established level where the source supports it.
    /// </summary>
    [JsonPropertyName("confersAssurance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AssuranceLevel? ConfersAssurance { get; set; }

    /// <summary>
    /// For <see cref="TrustSourceKind.DidAllowlist"/> — the explicit issuer DID URIs
    /// accepted (alsoKnownAs-equivalent issuers are also accepted).
    /// </summary>
    [JsonPropertyName("allowedIssuers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedIssuers { get; set; }

    /// <summary>
    /// For <see cref="TrustSourceKind.TrustList"/> — the snapshot identifier of the
    /// external trust list to consult.
    /// </summary>
    [JsonPropertyName("trustListId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustListId { get; set; }

    /// <summary>Source-specific tuning options (e.g. CRL mode).</summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Options { get; set; }
}
