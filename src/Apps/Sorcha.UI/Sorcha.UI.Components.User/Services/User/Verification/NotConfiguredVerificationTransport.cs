// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Models.Verification;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Default stub <see cref="IVerificationTransport"/> registered by <c>AddSorchaUserComponents</c>
/// when no host-specific transport has been wired (Feature 163, FR-004). Returns an explicit
/// not-configured sentinel — an empty <see cref="VerificationSessionStarted.SessionId"/> and
/// <see cref="VerificationSessionStarted.QrDeepLink"/> — so the component renders a
/// "verification is not yet wired" state without polling, throwing, or returning a fake pass.
/// A host replaces this with the real OID4VP transport (via <c>TryAdd*</c> override) in PR B3.
/// </summary>
public sealed class NotConfiguredVerificationTransport : IVerificationTransport
{
    /// <summary>
    /// Returns a sentinel started-result with empty <c>SessionId</c> and <c>QrDeepLink</c>;
    /// the component uses this to render the not-configured state without starting a poll loop.
    /// </summary>
    public Task<VerificationSessionStarted> StartSessionAsync(VerificationPreset question, CancellationToken ct = default)
        => Task.FromResult(new VerificationSessionStarted(
            SessionId: "",
            QrDeepLink: "",
            Purpose: question.Purpose,
            RequiredVct: question.RequiredVct));

    /// <summary>
    /// Returns a permanently-pending (non-completing) poll result; never called in practice
    /// because the component does not poll when the sentinel is detected.
    /// </summary>
    public Task<VerificationSessionPoll> PollSessionAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(new VerificationSessionPoll(IsComplete: false, VpToken: null, PresentationSubmission: null));
}
