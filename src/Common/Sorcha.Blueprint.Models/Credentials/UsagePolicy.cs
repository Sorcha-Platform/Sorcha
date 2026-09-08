// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Defines how many times a credential may be presented before being consumed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UsagePolicy
{
    /// <summary>
    /// Credential can be presented an unlimited number of times.
    /// </summary>
    [JsonStringEnumMemberName("Reusable")]
    Reusable = 0,

    /// <summary>
    /// Credential can be presented exactly once, then transitions to Consumed.
    /// </summary>
    [JsonStringEnumMemberName("SingleUse")]
    SingleUse = 1,

    /// <summary>
    /// Credential can be presented N times (see MaxPresentations), then transitions to Consumed.
    /// </summary>
    [JsonStringEnumMemberName("LimitedUse")]
    LimitedUse = 2
}
