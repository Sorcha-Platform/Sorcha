// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Models.Verification;

/// <summary>
/// Unified state snapshot of a verification session (Feature 164, B3 T004).
/// Used by <c>HaipVerificationTransport</c> internally and returned by its typed
/// <c>StartAsync</c>/<c>PollAsync</c> methods, which map to the B2 <see cref="Services.Verification.IVerificationTransport"/>
/// seam (StartSessionAsync/PollSessionAsync).
/// </summary>
/// <param name="SessionId">Opaque session identifier from the HAIP verifier.</param>
/// <param name="QrDeepLink">The <c>openid4vp://</c> deep link to render as a QR for the holder.</param>
/// <param name="State">Current lifecycle state of the session.</param>
/// <param name="VpToken">The raw <c>vp_token</c> once the holder has submitted; null while pending.</param>
/// <param name="Delegation">Optional delegation hint from the HAIP verifier; null in most cases.</param>
/// <param name="Error">Human-readable error detail when <see cref="State"/> is <see cref="VerificationSessionState.Error"/>.</param>
public sealed record VerificationSession(
    string SessionId,
    string QrDeepLink,
    VerificationSessionState State,
    string? VpToken = null,
    string? Delegation = null,
    string? Error = null);

/// <summary>Lifecycle state of an OID4VP verification session.</summary>
public enum VerificationSessionState
{
    /// <summary>Session is open; the holder has not yet submitted a presentation.</summary>
    Pending,
    /// <summary>Holder has submitted; <see cref="VerificationSession.VpToken"/> is populated.</summary>
    Complete,
    /// <summary>Session TTL elapsed before the holder responded.</summary>
    Expired,
    /// <summary>A transport or network fault occurred; see <see cref="VerificationSession.Error"/>.</summary>
    Error
}
