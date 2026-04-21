// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Register.Models.Observations;

/// <summary>
/// Observation pushed from Validator.Service into Register.Service on each docket seal
/// and (throttled) on mempool-depth change (Feature 108). Overwrites a single slot per
/// register — no history retained. Only accepted from callers whose validator key is on
/// the register's roster.
/// </summary>
/// <param name="RegisterId">Register this sealing progress applies to.</param>
/// <param name="LastSealedHeight">Latest docket height this validator has sealed locally.</param>
/// <param name="MempoolDepth">Current unverified-pool depth for this register.</param>
/// <param name="ObservedAt">When the validator produced this observation.</param>
public sealed record ValidatorSealingObservation(
    [property: Required, StringLength(255, MinimumLength = 1)]
    string RegisterId,

    [property: Range(0, long.MaxValue)]
    long LastSealedHeight,

    [property: Range(0, int.MaxValue)]
    int MempoolDepth,

    DateTimeOffset ObservedAt);
