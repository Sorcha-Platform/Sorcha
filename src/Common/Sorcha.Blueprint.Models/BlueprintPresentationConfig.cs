// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Per-blueprint configuration for the Timebound Presentation Lifecycle (Feature 111).
/// All fields are optional; null values defer to platform + register-visibility defaults.
/// </summary>
/// <param name="RecordAbandonment">When true, the abandonment sweeper writes a
/// PresentationAbandoned transaction on validity-window expiry. Default false —
/// low-stakes flows don't need abandonment records; the attempt record alone is
/// enough evidence of engagement. Set true for timebound/statutory flows where a
/// "definitively closed" signal matters.</param>
/// <param name="OutcomeDetailLevel">Controls how much verifier diagnostic info is
/// written to decline outcomes on the register. null = use the register-visibility
/// default (advertise=true → Minimal, advertise=false → Verbose).</param>
/// <param name="PresentationValidityWindowSeconds">Override for the validity
/// window during which a verifier callback can resolve the attempt. null = use
/// platform default (600s / 10 min).</param>
public sealed record BlueprintPresentationConfig(
    bool RecordAbandonment = false,
    OutcomeDetailLevel? OutcomeDetailLevel = null,
    int? PresentationValidityWindowSeconds = null);

/// <summary>
/// How much verifier diagnostic info is written to decline outcomes on the register.
/// </summary>
public enum OutcomeDetailLevel
{
    /// <summary>
    /// Only the PresentationDeclineReason code is written. Privacy-preserving default
    /// for registers with advertise=true (publicly subscribable).
    /// </summary>
    Minimal,

    /// <summary>
    /// Reason code plus consumer-provided verifier diagnostics are written.
    /// Debugging-friendly default for registers with advertise=false (invitation-only).
    /// </summary>
    Verbose
}
