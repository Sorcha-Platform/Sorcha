// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Level of identity assurance a credential establishes (feature 135). Ordered:
/// <c>Low &lt; Substantial &lt; High</c>. Numeric values are deliberately ascending so a
/// minimum-level comparison is a simple ordinal check. Defaults to <see cref="Low"/>
/// when no signal is present (fail-safe).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssuranceLevel
{
    /// <summary>
    /// No assurance — a signature-verified but unvouched issuer accepted under a reduced-assurance
    /// (Warn) policy (feature 177). Below <see cref="Low"/>, so it can never satisfy a minimum-level floor.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None = -1,

    /// <summary>Lowest assurance. The default when no signal is present.</summary>
    [JsonStringEnumMemberName("low")]
    Low = 0,

    /// <summary>Substantial assurance (eIDAS-aligned).</summary>
    [JsonStringEnumMemberName("substantial")]
    Substantial = 1,

    /// <summary>High assurance (eIDAS-aligned).</summary>
    [JsonStringEnumMemberName("high")]
    High = 2
}
