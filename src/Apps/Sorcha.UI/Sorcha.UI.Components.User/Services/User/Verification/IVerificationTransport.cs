// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Models.Verification;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Transport seam for the unified verify flow (Verify-unification PR B2). The shared verify control
/// depends on this to start a verification (returning a QR deep link to show the holder) and to poll
/// for the holder's submitted presentation. Each host wires its own implementation — both targeting
/// HAIP's <c>request_uri</c> / <c>direct-post</c> / result endpoints (the single transport) in PR B3.
/// The rich verdict is then computed client-side from the returned <c>vp_token</c>, so this seam is
/// deliberately verdict-free — it only delivers the raw presentation.
/// </summary>
public interface IVerificationTransport
{
    /// <summary>
    /// Starts a verification session for the given question and returns the session id plus the
    /// OpenID4VP deep link to render as a QR for the holder to scan.
    /// </summary>
    Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default);

    /// <summary>
    /// Polls for the holder's submission. Until the holder has presented, returns
    /// <see cref="VerificationSessionPoll.IsComplete"/> = false with a null token; once submitted,
    /// returns the raw <c>vp_token</c> (and <c>presentation_submission</c>) for client-side verdict
    /// computation.
    /// </summary>
    Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>Result of starting a verification session.</summary>
/// <param name="SessionId">Opaque id used to poll for the result.</param>
/// <param name="QrDeepLink">The <c>openid4vp://</c> deep link to render as a QR.</param>
/// <param name="Purpose">The purpose presented to the holder.</param>
/// <param name="RequiredVct">The credential type requested.</param>
public sealed record VerificationSessionStarted(
    string SessionId,
    string QrDeepLink,
    string Purpose,
    string RequiredVct);

/// <summary>Result of polling a verification session.</summary>
/// <param name="IsComplete">True once the holder has submitted a presentation.</param>
/// <param name="VpToken">The raw submitted <c>vp_token</c>, or null while pending.</param>
/// <param name="PresentationSubmission">The OID4VP <c>presentation_submission</c>, when present.</param>
/// <param name="IsTerminal">True when the session has reached a non-resumable state (Complete, Expired, or Error). The poll loop should stop when this is true.</param>
/// <param name="Outcome">
/// The verification verdict computed from the authoritative HAIP result, populated on completion.
/// Null while pending and for transports that do not produce a verdict (the not-configured stub).
/// </param>
public sealed record VerificationSessionPoll(
    bool IsComplete,
    string? VpToken,
    string? PresentationSubmission,
    bool IsTerminal = false,
    VerificationOutcome? Outcome = null);
