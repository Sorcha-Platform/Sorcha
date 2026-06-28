// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Models.Authentication;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Anonymous client for the three Feature 168 social-link step-up endpoints.
/// All calls use the link-pending token as the principal; no bearer session is attached.
/// </summary>
public interface IAnonymousSocialLinkClientService
{
    /// <summary>
    /// Begins the step-up challenge for the account addressed by <paramref name="linkPendingToken"/>.
    /// On success the result carries the proof method and any server-supplied payload (e.g. WebAuthn options).
    /// </summary>
    /// <param name="linkPendingToken">Opaque link-pending token from the URL fragment.</param>
    /// <param name="preferred">Preferred proof method; null lets the server select the strongest enrolled v1 method.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AnonymousLinkInitiateResult> InitiateAsync(
        string linkPendingToken,
        ChallengeMethod? preferred = null,
        CancellationToken ct = default);

    /// <summary>
    /// Submits the proof produced by the user.
    /// On success the result carries a single-use challenge token for the confirm call.
    /// </summary>
    /// <param name="linkPendingToken">Opaque link-pending token from the URL fragment.</param>
    /// <param name="method">The method that was initiated — must match the server's initiate response.</param>
    /// <param name="proof">Method-specific proof body: <c>{"code":"######"}</c> for TOTP; WebAuthn assertion for Passkey.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AnonymousLinkVerifyResult> VerifyAsync(
        string linkPendingToken,
        ChallengeMethod method,
        JsonElement proof,
        CancellationToken ct = default);

    /// <summary>
    /// Redeems the link-pending token together with the challenge token to complete the link
    /// and receive a new web session.
    /// </summary>
    /// <param name="linkPendingToken">Opaque link-pending token from the URL fragment.</param>
    /// <param name="challengeToken">Single-use challenge token from <see cref="VerifyAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AnonymousLinkConfirmResult> ConfirmAsync(
        string linkPendingToken,
        string challengeToken,
        CancellationToken ct = default);
}
