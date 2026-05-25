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
    Revoked,

    /// <summary>
    /// Validator automatically ejected by a deterministic on-chain rule (Feature 138 US3) — proven
    /// equivocation or a liveness-timeout. Distinct from operator-driven <see cref="Revoked"/>:
    /// ejection is sealed by a <c>control.validator.eject</c> / <c>control.validator.liveness-violation</c>
    /// transaction that every honest node produces and applies identically. Excluded from voting and
    /// from verification, like Revoked.
    /// </summary>
    Ejected
}
