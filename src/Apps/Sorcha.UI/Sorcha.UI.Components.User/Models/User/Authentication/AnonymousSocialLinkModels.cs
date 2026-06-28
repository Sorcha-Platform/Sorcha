// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Authentication;

/// <summary>
/// Outcome discriminators for the link-challenge initiate call.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InitiateOutcome
{
    /// <summary>Initiate succeeded; the prompt should render the indicated proof method.</summary>
    Ok = 0,

    /// <summary>The link-pending token has expired or is invalid (HTTP 401).</summary>
    Expired = 1,

    /// <summary>No v1-eligible proof method (passkey or TOTP) is enrolled on the target account (HTTP 400).</summary>
    UnsupportedV1Method = 2,

    /// <summary>The request was rate-limited (HTTP 429).</summary>
    RateLimited = 3,

    /// <summary>An unexpected transport or server error occurred.</summary>
    Failed = 4,
}

/// <summary>
/// Outcome discriminators for the link-confirm call.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConfirmOutcome
{
    /// <summary>Social identity linked and session tokens returned.</summary>
    Linked = 0,

    /// <summary>The link-pending token or challenge token has expired or is invalid (HTTP 401).</summary>
    Expired = 1,

    /// <summary>The proof or challenge was invalid (HTTP 401/403).</summary>
    ProofInvalid = 2,

    /// <summary>The social provider is already linked to a different account (HTTP 409).</summary>
    Conflict = 3,

    /// <summary>The request was rate-limited (HTTP 429).</summary>
    RateLimited = 4,

    /// <summary>An unexpected transport or server error occurred.</summary>
    Failed = 5,
}

/// <summary>
/// Opaque carrier of the staged link-required state captured from the URL fragment.
/// The token is treated as completely opaque — never parsed, logged, or displayed.
/// </summary>
/// <param name="LinkPendingToken">Short-lived server token used as the principal for all three link calls.</param>
public sealed record LinkPendingOutcome(string LinkPendingToken);

/// <summary>
/// Request body for <c>POST /api/auth/social/link/challenge/initiate</c>.
/// </summary>
/// <param name="LinkPendingToken">The opaque link-pending token.</param>
/// <param name="PreferredMethod">Optional preferred proof method; null lets the server pick the strongest available v1 method.</param>
public sealed record AnonymousLinkInitiateRequest(
    string LinkPendingToken,
    ChallengeMethod? PreferredMethod);

/// <summary>
/// Result of calling <c>POST /api/auth/social/link/challenge/initiate</c>.
/// </summary>
/// <param name="Method">The proof method the prompt should render.</param>
/// <param name="Payload">WebAuthn credential-request options for <c>Passkey</c>; null for <c>Totp</c>.</param>
/// <param name="Outcome">Discriminator describing whether the initiate call succeeded or why it failed.</param>
public sealed record AnonymousLinkInitiateResult(
    ChallengeMethod Method,
    JsonElement? Payload,
    InitiateOutcome Outcome);

/// <summary>
/// Request body for <c>POST /api/auth/social/link/challenge/verify</c>.
/// </summary>
/// <param name="LinkPendingToken">The opaque link-pending token.</param>
/// <param name="Method">The proof method used — must match what initiate returned.</param>
/// <param name="Proof">Method-specific proof: <c>{"code":"######"}</c> for TOTP; WebAuthn assertion for Passkey.</param>
public sealed record AnonymousLinkVerifyRequest(
    string LinkPendingToken,
    ChallengeMethod Method,
    JsonElement Proof);

/// <summary>
/// Result of calling <c>POST /api/auth/social/link/challenge/verify</c>.
/// </summary>
/// <param name="Succeeded">True when proof was accepted and <see cref="ChallengeToken"/> is populated.</param>
/// <param name="ChallengeToken">Single-use challenge token presented via <c>X-Auth-Challenge</c> at confirm; present only on success.</param>
/// <param name="Error">Failure reason when <see cref="Succeeded"/> is false.</param>
public sealed record AnonymousLinkVerifyResult(
    bool Succeeded,
    string? ChallengeToken,
    ChallengeVerifyError Error);

/// <summary>
/// Result of calling <c>POST /api/auth/social/link/confirm</c>.
/// </summary>
/// <param name="Outcome">Discriminator for the confirm result.</param>
/// <param name="AccessToken">JWT access token; present only when <see cref="Outcome"/> is <see cref="ConfirmOutcome.Linked"/>.</param>
/// <param name="RefreshToken">Refresh token; present only when <see cref="Outcome"/> is <see cref="ConfirmOutcome.Linked"/>.</param>
/// <param name="ExpiresIn">Seconds until the access token expires; present only on success.</param>
public sealed record AnonymousLinkConfirmResult(
    ConfirmOutcome Outcome,
    string? AccessToken,
    string? RefreshToken,
    int? ExpiresIn);
