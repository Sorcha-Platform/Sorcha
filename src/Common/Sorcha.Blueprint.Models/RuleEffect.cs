// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// JSON Forms rule effects for conditional display/interactivity
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleEffect
{
    /// <summary>
    /// Control is visible when condition is true, hidden otherwise
    /// </summary>
    [JsonStringEnumMemberName("SHOW")]
    SHOW,

    /// <summary>
    /// Control is hidden when condition is true, visible otherwise
    /// </summary>
    [JsonStringEnumMemberName("HIDE")]
    HIDE,

    /// <summary>
    /// Control is enabled when condition is true, disabled otherwise
    /// </summary>
    [JsonStringEnumMemberName("ENABLE")]
    ENABLE,

    /// <summary>
    /// Control is disabled when condition is true, enabled otherwise
    /// </summary>
    [JsonStringEnumMemberName("DISABLE")]
    DISABLE
}
