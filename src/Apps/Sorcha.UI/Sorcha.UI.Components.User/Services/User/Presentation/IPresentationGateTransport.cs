// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Transport-neutral state of a credential-gate presentation. Each transport maps its own state
/// vocabulary onto this; the card never sees a protocol-specific state.
/// </summary>
public enum GateOutcome
{
    /// <summary>No wallet interaction observed yet.</summary>
    Pending,

    /// <summary>A presentation arrived and is being verified.</summary>
    Submitted,

    /// <summary>Verified. Disclosed claims may now be fetched.</summary>
    Success,

    /// <summary>The holder refused, or verification failed.</summary>
    Declined,

    /// <summary>The request's validity window elapsed.</summary>
    Expired,

    /// <summary>The holder walked away without completing.</summary>
    Abandoned,

    /// <summary>
    /// The lifecycle has no such request, so waiting can never succeed. Distinct from
    /// <see cref="Expired"/> deliberately: reporting an unreachable request as expired sends the
    /// citizen off to inspect their wallet instead of telling them the fault is ours (#1325).
    /// </summary>
    Unreachable
}

/// <summary>Helpers over <see cref="GateOutcome"/>.</summary>
public static class GateOutcomes
{
    /// <summary>Whether no further transition is expected, so the wait must end.</summary>
    public static bool IsTerminal(GateOutcome outcome) => outcome
        is GateOutcome.Success or GateOutcome.Declined or GateOutcome.Expired
        or GateOutcome.Abandoned or GateOutcome.Unreachable;
}

/// <summary>
/// Protocol seam for a credential gate. The card owns the chrome — QR, states, claims table —
/// and an implementation of this owns the lifecycle behind it.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a reuse of <c>IVerificationTransport</c>, which is verdict-shaped: it returns a
/// vp_token plus an outcome for client-side verdict computation. A gate does not want a verdict, it
/// wants disclosed claims to prefill a form, obtained through a single-use token. Bending that seam
/// would add a third master to an interface already serving two hosts.
/// </para>
/// <para>
/// The two lifecycles differ in more than a URL: different state vocabularies, claims delivered
/// inline versus through a token-bound fetch, and hub-primary versus poll-only waiting. That is why
/// this is a transport seam rather than a <c>PresentationSource</c> switch inside the card.
/// </para>
/// </remarks>
public interface IPresentationGateTransport
{
    /// <summary>Which source this transport serves. The card selects on this.</summary>
    PresentationSource Source { get; }

    /// <summary>
    /// Waits until the presentation reaches a terminal outcome, reporting non-terminal
    /// transitions through <paramref name="progress"/> along the way.
    /// </summary>
    /// <param name="requestId">The presentation request to wait on.</param>
    /// <param name="progress">Optional sink for non-terminal transitions, e.g. Submitted.</param>
    /// <param name="ct">Cancels the wait; implementations return a terminal outcome, not a throw.</param>
    /// <remarks>
    /// Implementations MUST return <see cref="GateOutcome.Unreachable"/> rather than
    /// <see cref="GateOutcome.Expired"/> when the lifecycle holds no such request, and MUST NOT
    /// throw on transport failure — return a terminal outcome instead, so the card always has
    /// something honest to render.
    /// </remarks>
    Task<GateOutcome> WaitForOutcomeAsync(
        Guid requestId, IProgress<GateOutcome>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Fetches the disclosed claims after a successful outcome, or null when none are available.
    /// </summary>
    /// <param name="requestId">The presentation request that succeeded.</param>
    /// <param name="claimsFetchToken">Single-use token, where the transport uses one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// A transport that uses a single-use token MUST reuse the same token across retries — the
    /// Feature 127 endpoint consumes it even on a <c>pending</c> response, so a freshly-minted
    /// token per attempt would burn the one the caller was given.
    /// </remarks>
    Task<IReadOnlyDictionary<string, object?>?> FetchClaimsAsync(
        Guid requestId, string? claimsFetchToken, CancellationToken ct = default);
}
