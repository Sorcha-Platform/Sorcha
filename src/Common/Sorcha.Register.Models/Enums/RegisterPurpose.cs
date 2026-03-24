// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Classification of a register's intended use
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegisterPurpose
{
    /// <summary>
    /// User-created register for general-purpose data flow orchestration
    /// </summary>
    General = 0,

    /// <summary>
    /// Platform-internal register for system operations (e.g., governance, metadata)
    /// </summary>
    System = 1
}
