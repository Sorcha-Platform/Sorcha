// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Outcome returned by an <see cref="IPresentationConsumer"/> after verification.
/// Shape depends on <see cref="Kind"/>:
/// <list type="bullet">
///   <item><description><see cref="PresentationOutcomeKind.Success"/> — <see cref="VerifiedClaims"/> and
///     <see cref="PresentationSubmissionHash"/> MUST be populated; <see cref="Reason"/> MUST be null.</description></item>
///   <item><description><see cref="PresentationOutcomeKind.Decline"/> — <see cref="Reason"/> MUST be populated;
///     <see cref="VerifiedClaims"/> and <see cref="PresentationSubmissionHash"/> MUST be null.</description></item>
/// </list>
/// <see cref="VerifierDiagnostics"/> is always optional; Blueprint Service writes
/// it to the register only when the blueprint's OutcomeDetailLevel is Verbose.
/// </summary>
/// <param name="Kind">Success or Decline.</param>
/// <param name="VerifiedClaims">Claims returned by the verifier (filtered by the
/// consumer to the set the blueprint's requirements asked for). Populated only on success.</param>
/// <param name="Reason">Why the presentation was declined. Populated only on decline.</param>
/// <param name="VerifierDiagnostics">Optional consumer-defined diagnostic payload.
/// Blueprint Service logs it regardless and writes it to the register only when
/// OutcomeDetailLevel is Verbose. Consumers SHOULD NOT put PII here.</param>
/// <param name="PresentationSubmissionHash">SHA-256 of the raw presentation
/// submission the verifier processed. Populated only on success, for audit.</param>
public sealed record PresentationOutcome(
    PresentationOutcomeKind Kind,
    IReadOnlyDictionary<string, object>? VerifiedClaims,
    PresentationDeclineReason? Reason,
    IReadOnlyDictionary<string, object>? VerifierDiagnostics,
    string? PresentationSubmissionHash);
