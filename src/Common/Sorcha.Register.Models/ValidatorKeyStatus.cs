// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Status of a validator's signing key within the validator roster.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidatorKeyStatus
{
    /// <summary>
    /// Key is authorized for signing new dockets.
    /// </summary>
    Active,

    /// <summary>
    /// Key was replaced by a newer key. Can still verify historical dockets
    /// but must not be used for new signatures.
    /// </summary>
    Rotated,

    /// <summary>
    /// Key permanently revoked. Rejected for all verification purposes.
    /// </summary>
    Revoked
}
