// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// How the trust sources in a <see cref="TrustPolicy"/> are combined (feature 135).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustCombinator
{
    /// <summary>Accept if ANY source vouches at or above the minimum assurance level. Default.</summary>
    [JsonStringEnumMemberName("anyOf")]
    AnyOf = 0,

    /// <summary>Accept only if EVERY source vouches; an unreachable required source fails closed.</summary>
    [JsonStringEnumMemberName("allOf")]
    AllOf = 1
}
